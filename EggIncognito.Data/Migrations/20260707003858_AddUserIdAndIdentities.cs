using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EggIncognito.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserIdAndIdentities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // `users`/`identities` are shared with EggLedger on the same Postgres instance and its
            // migration does this same repoint: every step is guarded (IF EXISTS/IF NOT EXISTS) so whichever app boots first wins and the other no-ops.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pgcrypto;");

            migrationBuilder.Sql(WhenUsersExists(
                "ALTER TABLE users ADD COLUMN IF NOT EXISTS user_id UUID NOT NULL DEFAULT gen_random_uuid();"));

            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS identities (
                    user_id    UUID NOT NULL,
                    provider   TEXT NOT NULL,
                    subject    TEXT NOT NULL,
                    linked_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
                    PRIMARY KEY (provider, subject)
                );");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS ix_identities_user_id ON identities(user_id);");

            migrationBuilder.Sql(WhenUsersExists(@"
                INSERT INTO identities (user_id, provider, subject)
                SELECT user_id, 'discord', discord_id FROM users
                WHERE discord_id IS NOT NULL AND discord_id <> ''
                ON CONFLICT (provider, subject) DO NOTHING;"));

            migrationBuilder.Sql("ALTER TABLE capture_proxy_addrs ADD COLUMN IF NOT EXISTS user_id UUID;");
            migrationBuilder.Sql(WhenUsersExists(@"
                UPDATE capture_proxy_addrs SET user_id = u.user_id FROM users u
                WHERE capture_proxy_addrs.discord_id = u.discord_id AND capture_proxy_addrs.user_id IS NULL;"));
            migrationBuilder.Sql("ALTER TABLE capture_proxy_addrs ALTER COLUMN user_id SET NOT NULL;");

            migrationBuilder.Sql("ALTER TABLE capture_user_cas ADD COLUMN IF NOT EXISTS user_id UUID;");
            migrationBuilder.Sql(WhenUsersExists(@"
                UPDATE capture_user_cas SET user_id = u.user_id FROM users u
                WHERE capture_user_cas.discord_id = u.discord_id AND capture_user_cas.user_id IS NULL;"));
            migrationBuilder.Sql("ALTER TABLE capture_user_cas ALTER COLUMN user_id SET NOT NULL;");

            // EggLedger's sessions/blobs tables FK discord_id -> users(discord_id); drop defensively since Postgres refuses to drop the old PK while either still references it.
            migrationBuilder.Sql("ALTER TABLE IF EXISTS sessions DROP CONSTRAINT IF EXISTS sessions_discord_id_fkey;");
            migrationBuilder.Sql("ALTER TABLE IF EXISTS blobs DROP CONSTRAINT IF EXISTS blobs_discord_id_fkey;");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM pg_constraint con
                        JOIN pg_class rel ON rel.oid = con.conrelid
                        WHERE rel.relname = 'users' AND con.contype = 'p'
                          AND con.conname IN ('PK_users', 'users_pkey')
                    ) AND NOT EXISTS (
                        SELECT 1 FROM pg_constraint con
                        JOIN pg_class rel ON rel.oid = con.conrelid
                        JOIN pg_attribute attr ON attr.attrelid = rel.oid AND attr.attnum = ANY(con.conkey)
                        WHERE rel.relname = 'users' AND con.contype = 'p' AND attr.attname = 'user_id'
                    ) THEN
                        EXECUTE (
                            SELECT format('ALTER TABLE users DROP CONSTRAINT %I', con.conname)
                            FROM pg_constraint con
                            JOIN pg_class rel ON rel.oid = con.conrelid
                            WHERE rel.relname = 'users' AND con.contype = 'p'
                        );
                        ALTER TABLE users ADD PRIMARY KEY (user_id);
                    END IF;
                END $$;");

            migrationBuilder.Sql(WhenUsersExists(@"
                ALTER TABLE users ALTER COLUMN discord_id DROP NOT NULL;
                UPDATE users SET discord_id = NULL WHERE discord_id = '';
                CREATE UNIQUE INDEX IF NOT EXISTS ix_users_discord_id ON users(discord_id) WHERE discord_id IS NOT NULL;"));
        }

        private static string WhenUsersExists(string body) => $@"
            DO $do$
            BEGIN
                IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                           WHERE c.relname = 'users' AND c.relkind = 'r' AND n.nspname = current_schema()) THEN
                    {body}
                END IF;
            END $do$;";

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: `users`/`identities` are shared with EggLedger, which owns the same repoint and has no down path either.
        }
    }
}
