using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalApp.Common.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProgressTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProgressForms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DoctorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DraftQuestions = table.Column<string>(type: "jsonb", nullable: false),
                    LatestVersionNumber = table.Column<int>(type: "integer", nullable: false),
                    SourceFormId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProgressForms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProgressForms_Doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProgressForms_ProgressForms_SourceFormId",
                        column: x => x.SourceFormId,
                        principalTable: "ProgressForms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProgressFormVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FormId = table.Column<Guid>(type: "uuid", nullable: false),
                    DoctorId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Questions = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProgressFormVersions", x => x.Id);
                    table.CheckConstraint("CK_ProgressFormVersion_VersionNumber", "\"VersionNumber\" > 0");
                    table.ForeignKey(
                        name: "FK_ProgressFormVersions_Doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProgressFormVersions_ProgressForms_FormId",
                        column: x => x.FormId,
                        principalTable: "ProgressForms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProgressPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DoctorId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    FormId = table.Column<Guid>(type: "uuid", nullable: false),
                    FormVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Frequency = table.Column<int>(type: "integer", nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndReason = table.Column<int>(type: "integer", nullable: true),
                    ReplacedByPlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProgressPlans", x => x.Id);
                    table.CheckConstraint("CK_ProgressPlan_EndDate", "\"EndDate\" IS NULL OR \"EndDate\" >= \"StartDate\"");
                    table.CheckConstraint("CK_ProgressPlan_EndReason", "(\"EndedAt\" IS NULL) = (\"EndReason\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_ProgressPlans_Doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProgressPlans_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProgressPlans_ProgressFormVersions_FormVersionId",
                        column: x => x.FormVersionId,
                        principalTable: "ProgressFormVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProgressPlans_ProgressForms_FormId",
                        column: x => x.FormId,
                        principalTable: "ProgressForms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProgressPlans_ProgressPlans_ReplacedByPlanId",
                        column: x => x.ReplacedByPlanId,
                        principalTable: "ProgressPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProgressEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    FormVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodStartUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PeriodEndUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsLate = table.Column<bool>(type: "boolean", nullable: false),
                    Answers = table.Column<string>(type: "jsonb", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProgressEntries", x => x.Id);
                    table.CheckConstraint("CK_ProgressEntry_Period", "\"PeriodEndDate\" >= \"PeriodStartDate\" AND \"PeriodEndUtc\" > \"PeriodStartUtc\"");
                    table.CheckConstraint("CK_ProgressEntry_Submitted", "(\"Status\" = 1) = (\"SubmittedAt\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_ProgressEntries_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProgressEntries_ProgressFormVersions_FormVersionId",
                        column: x => x.FormVersionId,
                        principalTable: "ProgressFormVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProgressEntries_ProgressPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "ProgressPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProgressPlanGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    DoctorId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProgressPlanGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProgressPlanGrants_Doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProgressPlanGrants_ProgressPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "ProgressPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MediaAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    StagingKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PromotionKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    FinalKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Size = table.Column<long>(type: "bigint", nullable: true),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UploadExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadyAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AttachedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ObjectsDeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StagingDeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaAssets", x => x.Id);
                    table.CheckConstraint("CK_MediaAsset_Attached", "(\"State\" = 2) = (\"AttachedAt\" IS NOT NULL)");
                    table.CheckConstraint("CK_MediaAsset_Ready", "\"State\" IN (0, 3) OR (\"FinalKey\" IS NOT NULL AND \"Size\" IS NOT NULL AND \"Sha256\" IS NOT NULL AND \"ReadyAt\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_MediaAssets_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MediaAssets_ProgressEntries_EntryId",
                        column: x => x.EntryId,
                        principalTable: "ProgressEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_EntryId",
                table: "MediaAssets",
                column: "EntryId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_PatientId",
                table: "MediaAssets",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_StagingKey",
                table: "MediaAssets",
                column: "StagingKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_State_UploadExpiresAt",
                table: "MediaAssets",
                columns: new[] { "State", "UploadExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProgressEntries_FormVersionId",
                table: "ProgressEntries",
                column: "FormVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProgressEntries_PatientId_PeriodStartDate",
                table: "ProgressEntries",
                columns: new[] { "PatientId", "PeriodStartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ProgressEntries_PlanId_PeriodStartDate",
                table: "ProgressEntries",
                columns: new[] { "PlanId", "PeriodStartDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProgressForms_DoctorId",
                table: "ProgressForms",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_ProgressForms_SourceFormId",
                table: "ProgressForms",
                column: "SourceFormId");

            migrationBuilder.CreateIndex(
                name: "IX_ProgressFormVersions_DoctorId",
                table: "ProgressFormVersions",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_ProgressFormVersions_FormId_VersionNumber",
                table: "ProgressFormVersions",
                columns: new[] { "FormId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProgressPlanGrants_DoctorId",
                table: "ProgressPlanGrants",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_ProgressPlanGrants_PlanId_DoctorId",
                table: "ProgressPlanGrants",
                columns: new[] { "PlanId", "DoctorId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProgressPlans_DoctorId",
                table: "ProgressPlans",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_ProgressPlans_FormId",
                table: "ProgressPlans",
                column: "FormId");

            migrationBuilder.CreateIndex(
                name: "IX_ProgressPlans_FormVersionId",
                table: "ProgressPlans",
                column: "FormVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProgressPlans_PatientId_DoctorId",
                table: "ProgressPlans",
                columns: new[] { "PatientId", "DoctorId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProgressPlans_ReplacedByPlanId",
                table: "ProgressPlans",
                column: "ReplacedByPlanId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaAssets");

            migrationBuilder.DropTable(
                name: "ProgressPlanGrants");

            migrationBuilder.DropTable(
                name: "ProgressEntries");

            migrationBuilder.DropTable(
                name: "ProgressPlans");

            migrationBuilder.DropTable(
                name: "ProgressFormVersions");

            migrationBuilder.DropTable(
                name: "ProgressForms");
        }
    }
}
