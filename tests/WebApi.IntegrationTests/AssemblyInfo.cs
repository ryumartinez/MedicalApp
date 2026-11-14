[assembly: CaptureConsole]
[assembly: CaptureTrace]

// Each collection starts its own API host, and FastEndpoints keeps static state such as the event bus's service
// resolver. Hosts running at the same time would publish domain events into the other host's database.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
