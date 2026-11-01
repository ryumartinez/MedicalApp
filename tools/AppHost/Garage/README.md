# Local Garage storage

Aspire runs a single-node Garage cluster for private media storage.
The pinned image is `dxflrs/garage:v2.4.1` (see `GarageImage.cs`).
Version 2.5.0 was one week old when this version was selected, so the earlier stable release was chosen.
The configuration is `garage.toml`.
It was validated with the Garage admin API v2 and `AWSSDK.S3` 4.0.104.3.

## Resources

| Resource | Purpose |
|---|---|
| `garage` | Garage container. The S3 API uses the stable host port `3900`. The admin API uses a random host port. |
| `garage-init` | Initializes the layout, bucket, keys, and CORS, then exits. |
| `api` | Starts after `garage-init` completes and Garage reports healthy. |

Garage keeps metadata in `/var/lib/garage/meta` and objects in `/var/lib/garage/data`.
Each path uses its own named Docker volume.
`Garage:MetadataVolume` and `Garage:DataVolume` can select other volume names.
Changing a name selects different data; it does not migrate data.

Garage reports healthy only after the layout gives the cluster quorum.
The initializer therefore waits for the container to start, not for it to be healthy.

## Initialization

`garage-init` runs `tools/GarageInitializer/GarageReconciler.cs` on every start.
Integration tests run the same code against the same image and configuration.
Each step checks existing state first, so a repeated or interrupted run is safe:

1. Assign the single node a role and apply layout version 1, or apply a staged change from an interrupted run.
2. Wait for `/health` to report quorum.
3. Create the private `medicalapp-media` bucket. Website access stays disabled.
4. Import the API key with read and write permission on that bucket only.
   The key cannot create buckets or change bucket settings.
5. Create the `medicalapp-init` key with owner permission only. It cannot read or write objects.
6. Set the bucket CORS rules with the initializer key.
7. Write, read, and delete a probe object with the API key.
   The API starts only after this probe succeeds.

## Credentials

Aspire generates these secret parameters and stores them in the AppHost secret store:

| Parameter | Use |
|---|---|
| `garage-rpc-secret` | Cluster RPC secret, passed as `GARAGE_RPC_SECRET` |
| `garage-admin-token` | Admin API bearer token, passed as `GARAGE_ADMIN_TOKEN`; only the initializer receives it |
| `garage-access-key-id` | API access key ID, used as `GK` followed by the parameter |
| `garage-secret-access-key` | API secret access key |

Generated key values contain only digits so they are valid hexadecimal for Garage.
The configuration file contains no secrets.
The API receives its key as `Storage:Garage:AccessKeyId` and `Storage:Garage:SecretAccessKey`.
If a parameter changes after the first start, the initializer imports a new API key; delete the old key with the Garage CLI if it is no longer needed.

## Endpoints and CORS

The API and the browser both use the published S3 endpoint, `http://localhost:3900`.
The API passes it as `Storage:Garage:ServiceUrl` and `Storage:Garage:PublicServiceUrl`.
Signed URLs use path-style addressing and the `garage` region.

The CORS rule allows `PUT`, `GET`, and `HEAD` from `Garage:CorsOrigins`, which defaults to `https://localhost:7255`.
Set a comma-separated list for other browser origins, such as a future frontend.
Requests may send `content-type` and `content-length`, and responses expose `ETag`.

Objects are private.
Anonymous requests return `403`.
A signed URL works for one key, one method, and, for uploads, one content type.
Signed URLs cannot limit upload size, so finalization validates the stored size.

## Exact-content promotion

Uploads go to a staging key that a signed URL can overwrite until it expires.
Finalization must store exactly the bytes it validated, at a key no client can write.
Validation against `v2.4.1` showed these Garage behaviors:

- Conditional copy with `x-amz-copy-source-if-match` works, but ETags are MD5 digests. MD5 collisions are practical, so a matching ETag does not prove identical content.
- `If-None-Match: *` on `PutObject` does not prevent overwrites.
- Requested SHA-256 checksums are not returned.

The API therefore takes a private snapshot with `ExactContentPromoter`.
It reads the staging object once into a temporary file, validates that file, and uploads the same file to a separate final key.
The upload carries an MD5 digest that Garage checks in transit.
The API stores the SHA-256 digest of the validated bytes.
A concurrent staging replacement either finishes before the read, so the new bytes are validated, or after it, so it has no effect.
`GarageStorageTests.PromotionStoresExactlyTheValidatedBytesDespiteStagingReplacement` checks this against real Garage.

## Media lifecycle

| State | Meaning | Next step |
|---|---|---|
| Pending | An upload URL was issued for the staging key. | The patient uploads and calls finalize. |
| Ready | The validated bytes are at a final key. A draft answer can reference the asset. | Submission attaches it. |
| Attached | A submitted entry references the asset. | None. Attached media is never removed. |
| Expired | The upload or the unattached file was abandoned. | Cleanup removes its objects. |

Upload URLs last 15 minutes and download URLs last five.
A signed URL cannot be revoked; anyone holding one can use it until it expires.
Finalization stays possible for 24 hours after the upload URL expires.
A ready asset that no submitted entry attaches expires seven days after it became ready.

## Submission and conflicts

Submission, plan closure, and treatment revocation coordinate through row locks in a fixed order: treatment, plans, entry, then media.
Whichever of closure and submission commits first decides the outcome; submission after a committed closure returns `409`.
Stale revisions for forms, plans, and entries return `409` and keep the committed state; reload and retry.
A submission retry after a commit returns the stored entry with `200` and writes nothing.

## Cleanup

The API's `MediaCleanupService` runs every five minutes without patient requests.
`Media:Cleanup:Interval`, `Media:Cleanup:BatchSize`, and `Media:Cleanup:Enabled` change its behavior.
Each run:

1. Claims expirable assets with `FOR UPDATE SKIP LOCKED` and commits their Expired state.
   A submission that holds an asset's lock is skipped, and an attached asset is never eligible.
   An expiry that commits first makes submission reject the asset.
2. Deletes the staging, promotion, and final objects of expired assets outside any transaction, recording success per asset.
3. Deletes the staging copies of ready and attached assets. Their final objects stay.

A storage failure leaves the asset marked for deletion, and the next run retries.
Finalization also records the final key before writing it, so an interrupted finalization's object is removed by its retry or by cleanup.
Logs report counts for each run that changed something.

## Restart behavior

Restarting the AppHost recreates the container with the same volumes.
The node keeps its identity and layout, the bucket and keys remain, and stored objects keep their bytes.
The initializer finds every step complete and only refreshes CORS and the probe.
`GarageStorageTests.MetadataAndBytesSurviveContainerReplacement` checks this with a replaced container.
Do not delete the volumes to recover from an error; rerun the AppHost instead.

## Production

Publish and deploy operations do not create these resources.
Production storage durability, deployment, and backups require a separate deployment plan.
