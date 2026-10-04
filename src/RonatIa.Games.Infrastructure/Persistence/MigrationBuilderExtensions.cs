using Microsoft.EntityFrameworkCore.Migrations;

namespace RonatIa.Games.Infrastructure.Persistence;

public static class MigrationBuilderExtensions
{
    /// <summary>
    /// Habilita Row Level Security em todas as tabelas já existentes do schema, sem criar políticas: quem não é dono
    /// da tabela (por exemplo, as funções <c>anon</c> e <c>authenticated</c> da Data API do Supabase) não enxerga nada.
    /// É uma segunda barreira; o backend, que conecta como dono, não é afetado. Chame ao final de toda migração que cria tabelas
    /// (há um teste que falha se alguma tabela ficar sem RLS).
    /// </summary>
    public static MigrationBuilder EnableRowLevelSecurityOnAllTables(this MigrationBuilder migrationBuilder, string schema)
    {
        migrationBuilder.Sql($$"""
            DO $$
            DECLARE r record;
            BEGIN
              FOR r IN SELECT tablename FROM pg_tables WHERE schemaname = '{{schema}}' LOOP
                EXECUTE format('ALTER TABLE %I.%I ENABLE ROW LEVEL SECURITY', '{{schema}}', r.tablename);
              END LOOP;
            END $$;
            """);

        return migrationBuilder;
    }
}
