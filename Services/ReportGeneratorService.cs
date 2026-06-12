using System.Text;

namespace EnovaMigrator.Services;

/// <summary>
/// Generator raportów podsumowujących migrację w formacie HTML.
/// </summary>
public class ReportGeneratorService
{
    /// <summary>
    /// Generuje raport HTML z wynikami migracji.
    /// </summary>
    public string GenerateHtmlReport(MigrationResult result, AuditLogService? auditLog = null)
    {
        var sb = new StringBuilder();

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"pl\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"UTF-8\">");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine("  <title>Raport migracji EnovaMigrator</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine(GetCssStyles());
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        // Nagłówek
        sb.AppendLine("  <div class=\"container\">");
        sb.AppendLine("    <header>");
        sb.AppendLine("      <h1>Raport migracji EnovaMigrator</h1>");
        sb.AppendLine($"      <p class=\"date\">Wygenerowano: {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>");
        sb.AppendLine("    </header>");

        // Status
        var statusClass = result.Success ? "success" : "error";
        var statusText = result.Success ? "SUKCES" : "BŁĄD";
        sb.AppendLine($"    <div class=\"status {statusClass}\">");
        sb.AppendLine($"      <h2>Status: {statusText}</h2>");
        if (!result.Success && !string.IsNullOrEmpty(result.ErrorMessage))
        {
            sb.AppendLine($"      <p class=\"error-message\">{EscapeHtml(result.ErrorMessage)}</p>");
        }
        sb.AppendLine("    </div>");

        // Statystyki główne
        sb.AppendLine("    <section class=\"summary\">");
        sb.AppendLine("      <h2>Podsumowanie</h2>");
        sb.AppendLine("      <div class=\"stats-grid\">");
        sb.AppendLine(GenerateStatCard("Pracownicy", result.Stats.PracownicyMigrated, result.Stats.PracownicySkipped, result.Stats.PracownicyErrors));
        sb.AppendLine(GenerateStatCard("Umowy", result.Stats.UmowyMigrated, result.Stats.UmowySkipped, result.Stats.UmowyErrors));
        sb.AppendLine(GenerateStatCard("Listy płac", result.Stats.ListyPlacMigrated, result.Stats.ListyPlacSkipped, result.Stats.ListyPlacErrors));
        sb.AppendLine(GenerateStatCard("Wypłaty", result.Stats.WyplatyMigrated, result.Stats.WyplatySkipped, result.Stats.WyplatyErrors));
        sb.AppendLine(GenerateStatCard("Elementy wypłat", result.Stats.WypElementyMigrated, result.Stats.WypElementySkipped, result.Stats.WypElementyErrors));
        sb.AppendLine(GenerateStatCard("Nieobecności", result.Stats.NieobecnosciMigrated, result.Stats.NieobecnosciSkipped, result.Stats.NieobecnosciErrors));
        sb.AppendLine(GenerateStatCard("Rodzina", result.Stats.RodzinaMigrated, result.Stats.RodzinaSkipped, result.Stats.RodzinaErrors));
        sb.AppendLine(GenerateStatCard("Dodatki", result.Stats.DodatkiMigrated, result.Stats.DodatkiSkipped, result.Stats.DodatkiErrors));
        sb.AppendLine("      </div>");
        sb.AppendLine("    </section>");

        // Tabela szczegółowa
        sb.AppendLine("    <section class=\"details\">");
        sb.AppendLine("      <h2>Szczegóły per tabela</h2>");
        sb.AppendLine("      <table>");
        sb.AppendLine("        <thead>");
        sb.AppendLine("          <tr><th>Tabela</th><th>Razem</th><th>Zmigrowano</th><th>Pominięto</th><th>Błędy</th></tr>");
        sb.AppendLine("        </thead>");
        sb.AppendLine("        <tbody>");
        sb.AppendLine(GenerateTableRow("Pracownicy", result.Stats.PracownicyTotal, result.Stats.PracownicyMigrated, result.Stats.PracownicySkipped, result.Stats.PracownicyErrors));
        sb.AppendLine(GenerateTableRow("Umowy", result.Stats.UmowyTotal, result.Stats.UmowyMigrated, result.Stats.UmowySkipped, result.Stats.UmowyErrors));
        sb.AppendLine(GenerateTableRow("Listy płac", result.Stats.ListyPlacTotal, result.Stats.ListyPlacMigrated, result.Stats.ListyPlacSkipped, result.Stats.ListyPlacErrors));
        sb.AppendLine(GenerateTableRow("Wypłaty", result.Stats.WyplatyTotal, result.Stats.WyplatyMigrated, result.Stats.WyplatySkipped, result.Stats.WyplatyErrors));
        sb.AppendLine(GenerateTableRow("Elementy wypłat", result.Stats.WypElementyTotal, result.Stats.WypElementyMigrated, result.Stats.WypElementySkipped, result.Stats.WypElementyErrors));
        sb.AppendLine(GenerateTableRow("Nieobecności", result.Stats.NieobecnosciTotal, result.Stats.NieobecnosciMigrated, result.Stats.NieobecnosciSkipped, result.Stats.NieobecnosciErrors));
        sb.AppendLine(GenerateTableRow("Rodzina", result.Stats.RodzinaTotal, result.Stats.RodzinaMigrated, result.Stats.RodzinaSkipped, result.Stats.RodzinaErrors));
        sb.AppendLine(GenerateTableRow("Dodatki", result.Stats.DodatkiTotal, result.Stats.DodatkiMigrated, result.Stats.DodatkiSkipped, result.Stats.DodatkiErrors));
        sb.AppendLine(GenerateTableRow("Adresy", result.Stats.AdresyTotal, result.Stats.AdresyMigrated, result.Stats.AdresySkipped, result.Stats.AdresyErrors));
        sb.AppendLine(GenerateTableRow("Rachunki bank.", result.Stats.RachunkiTotal, result.Stats.RachunkiMigrated, result.Stats.RachunkiSkipped, result.Stats.RachunkiErrors));
        sb.AppendLine(GenerateTableRow("Historia kadr.", result.Stats.PracHistorieTotal, result.Stats.PracHistorieMigrated, result.Stats.PracHistorieSkipped, result.Stats.PracHistorieErrors));
        sb.AppendLine(GenerateTableRow("Kalendarze", result.Stats.KalendarzeTotal, result.Stats.KalendarzeMigrated, result.Stats.KalendarzeSkipped, result.Stats.KalendarzeErrors));
        sb.AppendLine(GenerateTableRow("Hist. zatrudn.", result.Stats.HistZatrudnienTotal, result.Stats.HistZatrudnienMigrated, result.Stats.HistZatrudnienSkipped, result.Stats.HistZatrudnienErrors));
        sb.AppendLine("        </tbody>");
        sb.AppendLine("      </table>");
        sb.AppendLine("    </section>");

        // Błędy
        if (result.Stats.Errors.Any())
        {
            sb.AppendLine("    <section class=\"errors\">");
            sb.AppendLine("      <h2>Błędy</h2>");
            sb.AppendLine("      <ul>");
            foreach (var error in result.Stats.Errors.Take(50))
            {
                sb.AppendLine($"        <li>{EscapeHtml(error)}</li>");
            }
            if (result.Stats.Errors.Count > 50)
            {
                sb.AppendLine($"        <li>... i {result.Stats.Errors.Count - 50} więcej</li>");
            }
            sb.AppendLine("      </ul>");
            sb.AppendLine("    </section>");
        }

        // Metryki wydajności
        if (auditLog != null)
        {
            var metrics = auditLog.GetPerformanceMetrics();
            sb.AppendLine("    <section class=\"performance\">");
            sb.AppendLine("      <h2>Wydajność</h2>");
            sb.AppendLine("      <div class=\"perf-stats\">");
            sb.AppendLine($"        <div class=\"perf-item\"><span class=\"label\">Czas całkowity:</span> <span class=\"value\">{metrics.TotalDurationSeconds:F1} sek</span></div>");
            sb.AppendLine($"        <div class=\"perf-item\"><span class=\"label\">Rekordy/sek:</span> <span class=\"value\">{metrics.RecordsPerSecond:F1}</span></div>");
            sb.AppendLine($"        <div class=\"perf-item\"><span class=\"label\">Zmigrowano:</span> <span class=\"value\">{metrics.TotalRecordsMigrated}</span></div>");
            sb.AppendLine("      </div>");
            sb.AppendLine("    </section>");
        }

        // Stopka
        sb.AppendLine("    <footer>");
        sb.AppendLine("      <p>EnovaMigrator - narzędzie do migracji danych kadrowo-płacowych enova365</p>");
        sb.AppendLine("    </footer>");
        sb.AppendLine("  </div>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    /// <summary>
    /// Zapisuje raport HTML do pliku.
    /// </summary>
    public string SaveHtmlReport(MigrationResult result, AuditLogService? auditLog = null, string? fileName = null)
    {
        var html = GenerateHtmlReport(result, auditLog);
        var filePath = fileName ?? $"migration_report_{DateTime.Now:yyyyMMdd_HHmmss}.html";
        File.WriteAllText(filePath, html, Encoding.UTF8);
        return filePath;
    }

    private string GenerateStatCard(string title, int migrated, int skipped, int errors)
    {
        var total = migrated + skipped + errors;
        var successRate = total > 0 ? (migrated * 100.0 / total) : 100;
        var cardClass = errors > 0 ? "has-errors" : (migrated > 0 ? "has-success" : "neutral");

        return $@"
        <div class=""stat-card {cardClass}"">
          <h3>{title}</h3>
          <div class=""stat-number"">{migrated}</div>
          <div class=""stat-details"">
            <span class=""skipped"">Pominięto: {skipped}</span>
            <span class=""errors"">Błędy: {errors}</span>
          </div>
          <div class=""progress-bar"">
            <div class=""progress"" style=""width: {successRate:F0}%""></div>
          </div>
        </div>";
    }

    private string GenerateTableRow(string table, int total, int migrated, int skipped, int errors)
    {
        var rowClass = errors > 0 ? "error-row" : "";
        return $"          <tr class=\"{rowClass}\"><td>{table}</td><td>{total}</td><td>{migrated}</td><td>{skipped}</td><td>{errors}</td></tr>";
    }

    private static string EscapeHtml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }

    private string GetCssStyles()
    {
        return @"
    * { box-sizing: border-box; margin: 0; padding: 0; }
    body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: #f5f5f5; color: #333; line-height: 1.6; }
    .container { max-width: 1200px; margin: 0 auto; padding: 20px; }
    header { text-align: center; margin-bottom: 30px; }
    header h1 { color: #2c3e50; }
    header .date { color: #7f8c8d; }

    .status { padding: 20px; border-radius: 8px; text-align: center; margin-bottom: 30px; }
    .status.success { background: #d4edda; color: #155724; }
    .status.error { background: #f8d7da; color: #721c24; }
    .error-message { margin-top: 10px; font-family: monospace; }

    section { background: white; padding: 20px; border-radius: 8px; margin-bottom: 20px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }
    section h2 { color: #2c3e50; margin-bottom: 15px; border-bottom: 2px solid #3498db; padding-bottom: 10px; }

    .stats-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 15px; }
    .stat-card { padding: 15px; border-radius: 8px; background: #f8f9fa; text-align: center; }
    .stat-card.has-success { border-left: 4px solid #27ae60; }
    .stat-card.has-errors { border-left: 4px solid #e74c3c; }
    .stat-card h3 { font-size: 14px; color: #7f8c8d; margin-bottom: 5px; }
    .stat-number { font-size: 32px; font-weight: bold; color: #2c3e50; }
    .stat-details { font-size: 12px; color: #7f8c8d; margin-top: 5px; }
    .stat-details .errors { color: #e74c3c; margin-left: 10px; }
    .progress-bar { height: 4px; background: #ecf0f1; border-radius: 2px; margin-top: 10px; }
    .progress { height: 100%; background: #27ae60; border-radius: 2px; }

    table { width: 100%; border-collapse: collapse; }
    th, td { padding: 12px; text-align: left; border-bottom: 1px solid #ecf0f1; }
    th { background: #3498db; color: white; }
    tr:hover { background: #f5f5f5; }
    tr.error-row { background: #fef0f0; }

    .errors ul { list-style: none; max-height: 300px; overflow-y: auto; }
    .errors li { padding: 8px; background: #fef0f0; margin-bottom: 5px; border-radius: 4px; font-family: monospace; font-size: 13px; }

    .performance .perf-stats { display: flex; gap: 30px; flex-wrap: wrap; }
    .perf-item { display: flex; gap: 10px; }
    .perf-item .label { color: #7f8c8d; }
    .perf-item .value { font-weight: bold; color: #2c3e50; }

    footer { text-align: center; color: #7f8c8d; padding: 20px; }
    ";
    }
}
