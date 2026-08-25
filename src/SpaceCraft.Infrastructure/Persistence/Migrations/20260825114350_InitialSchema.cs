using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SpaceCraft.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "spacecraft");

            migrationBuilder.CreateTable(
                name: "organizations",
                schema: "spacecraft",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    slug = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organizations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pipeline_runs",
                schema: "spacecraft",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    api_project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    spec_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sdk_target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    triggered_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trigger = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    publish_requested = table.Column<bool>(type: "boolean", nullable: false),
                    resolved_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    queued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    claimed_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    max_attempts = table.Column<int>(type: "integer", nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    artifact_storage_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pipeline_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "publications",
                schema: "spacecraft",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pipeline_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sdk_target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    registry = table.Column<int>(type: "integer", nullable: false),
                    package_name = table.Column<string>(type: "character varying(214)", maxLength: 214, nullable: false),
                    version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    registry_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_publications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                schema: "spacecraft",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "registry_credentials",
                schema: "spacecraft",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    registry = table.Column<int>(type: "integer", nullable: false),
                    label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    cipher_text = table.Column<byte[]>(type: "bytea", nullable: false),
                    last_four = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_revoked = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_registry_credentials", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "spacecraft",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "spacecraft",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    github_user_id = table.Column<string>(type: "text", nullable: true),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "waitlist_entries",
                schema: "spacecraft",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    confirmation_token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    is_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ip_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    unsubscribed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_waitlist_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "api_projects",
                schema: "spacecraft",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    slug = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    github_repository = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    github_spec_path = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    github_branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_projects", x => x.id);
                    table.ForeignKey(
                        name: "fk_api_projects_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "spacecraft",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "organization_members",
                schema: "spacecraft",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organization_members", x => x.id);
                    table.ForeignKey(
                        name: "fk_organization_members_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "spacecraft",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pipeline_steps",
                schema: "spacecraft",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pipeline_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    log_storage_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pipeline_steps", x => x.id);
                    table.ForeignKey(
                        name: "fk_pipeline_steps_pipeline_runs_pipeline_run_id",
                        column: x => x.pipeline_run_id,
                        principalSchema: "spacecraft",
                        principalTable: "pipeline_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "role_claims",
                schema: "spacecraft",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_role_claims_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "spacecraft",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_claims",
                schema: "spacecraft",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_claims_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "spacecraft",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_logins",
                schema: "spacecraft",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_user_logins_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "spacecraft",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                schema: "spacecraft",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_roles", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_user_roles_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "spacecraft",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_roles_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "spacecraft",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_tokens",
                schema: "spacecraft",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_user_tokens_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "spacecraft",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sdk_targets",
                schema: "spacecraft",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    api_project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    language = table.Column<int>(type: "integer", nullable: false),
                    package_name = table.Column<string>(type: "character varying(214)", maxLength: 214, nullable: false),
                    registry = table.Column<int>(type: "integer", nullable: false),
                    auto_publish = table.Column<bool>(type: "boolean", nullable: false),
                    last_published_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    last_published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sdk_targets", x => x.id);
                    table.ForeignKey(
                        name: "fk_sdk_targets_api_projects_api_project_id",
                        column: x => x.api_project_id,
                        principalSchema: "spacecraft",
                        principalTable: "api_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "spec_versions",
                schema: "spacecraft",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    api_project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    size_in_bytes = table.Column<long>(type: "bigint", nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    validation_status = table.Column<int>(type: "integer", nullable: false),
                    diagnostics = table.Column<string>(type: "jsonb", nullable: true),
                    warning_count = table.Column<int>(type: "integer", nullable: false),
                    error_count = table.Column<int>(type: "integer", nullable: false),
                    api_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    api_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    git_commit_sha = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_spec_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_spec_versions_api_projects_api_project_id",
                        column: x => x.api_project_id,
                        principalSchema: "spacecraft",
                        principalTable: "api_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_api_projects_github_repository",
                schema: "spacecraft",
                table: "api_projects",
                column: "github_repository",
                filter: "github_repository IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_api_projects_organization_id_slug",
                schema: "spacecraft",
                table: "api_projects",
                columns: new[] { "organization_id", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_organization_members_organization_id_user_id",
                schema: "spacecraft",
                table: "organization_members",
                columns: new[] { "organization_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_organization_members_user_id",
                schema: "spacecraft",
                table: "organization_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_organizations_slug",
                schema: "spacecraft",
                table: "organizations",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pipeline_runs_api_project_id_queued_at",
                schema: "spacecraft",
                table: "pipeline_runs",
                columns: new[] { "api_project_id", "queued_at" });

            migrationBuilder.CreateIndex(
                name: "ix_pipeline_runs_lease",
                schema: "spacecraft",
                table: "pipeline_runs",
                column: "lease_expires_at",
                filter: "lease_expires_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_pipeline_runs_organization_id",
                schema: "spacecraft",
                table: "pipeline_runs",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_pipeline_runs_queue",
                schema: "spacecraft",
                table: "pipeline_runs",
                columns: new[] { "status", "queued_at" },
                filter: "status = 1");

            migrationBuilder.CreateIndex(
                name: "ix_pipeline_steps_pipeline_run_id_ordinal",
                schema: "spacecraft",
                table: "pipeline_steps",
                columns: new[] { "pipeline_run_id", "ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_publications_organization_id",
                schema: "spacecraft",
                table: "publications",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_publications_pipeline_run_id",
                schema: "spacecraft",
                table: "publications",
                column: "pipeline_run_id");

            migrationBuilder.CreateIndex(
                name: "ux_publications_target_version",
                schema: "spacecraft",
                table: "publications",
                columns: new[] { "sdk_target_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_family_id",
                schema: "spacecraft",
                table: "refresh_tokens",
                column: "family_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                schema: "spacecraft",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id",
                schema: "spacecraft",
                table: "refresh_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_registry_credentials_organization_id_registry",
                schema: "spacecraft",
                table: "registry_credentials",
                columns: new[] { "organization_id", "registry" });

            migrationBuilder.CreateIndex(
                name: "ix_role_claims_role_id",
                schema: "spacecraft",
                table: "role_claims",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "role_name_index",
                schema: "spacecraft",
                table: "roles",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sdk_targets_api_project_id_language",
                schema: "spacecraft",
                table: "sdk_targets",
                columns: new[] { "api_project_id", "language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sdk_targets_registry_package_name",
                schema: "spacecraft",
                table: "sdk_targets",
                columns: new[] { "registry", "package_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_spec_versions_api_project_id_content_hash",
                schema: "spacecraft",
                table: "spec_versions",
                columns: new[] { "api_project_id", "content_hash" });

            migrationBuilder.CreateIndex(
                name: "ix_spec_versions_api_project_id_sequence",
                schema: "spacecraft",
                table: "spec_versions",
                columns: new[] { "api_project_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_claims_user_id",
                schema: "spacecraft",
                table: "user_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_logins_user_id",
                schema: "spacecraft",
                table: "user_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_role_id",
                schema: "spacecraft",
                table: "user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "email_index",
                schema: "spacecraft",
                table: "users",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "user_name_index",
                schema: "spacecraft",
                table: "users",
                column: "normalized_user_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_entries_email",
                schema: "spacecraft",
                table: "waitlist_entries",
                column: "email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "organization_members",
                schema: "spacecraft");

            migrationBuilder.DropTable(
                name: "pipeline_steps",
                schema: "spacecraft");

            migrationBuilder.DropTable(
                name: "publications",
                schema: "spacecraft");

            migrationBuilder.DropTable(
                name: "refresh_tokens",
                schema: "spacecraft");

            migrationBuilder.DropTable(
                name: "registry_credentials",
                schema: "spacecraft");

            migrationBuilder.DropTable(
                name: "role_claims",
                schema: "spacecraft");

            migrationBuilder.DropTable(
                name: "sdk_targets",
                schema: "spacecraft");

            migrationBuilder.DropTable(
                name: "spec_versions",
                schema: "spacecraft");

            migrationBuilder.DropTable(
                name: "user_claims",
                schema: "spacecraft");

            migrationBuilder.DropTable(
                name: "user_logins",
                schema: "spacecraft");

            migrationBuilder.DropTable(
                name: "user_roles",
                schema: "spacecraft");

            migrationBuilder.DropTable(
                name: "user_tokens",
                schema: "spacecraft");

            migrationBuilder.DropTable(
                name: "waitlist_entries",
                schema: "spacecraft");

            migrationBuilder.DropTable(
                name: "pipeline_runs",
                schema: "spacecraft");

            migrationBuilder.DropTable(
                name: "api_projects",
                schema: "spacecraft");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "spacecraft");

            migrationBuilder.DropTable(
                name: "users",
                schema: "spacecraft");

            migrationBuilder.DropTable(
                name: "organizations",
                schema: "spacecraft");
        }
    }
}
