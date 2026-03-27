using System.Net;
using System.Text;
using GestionStock.Web.Models;
using Microsoft.JSInterop;

namespace GestionStock.Web.Services;

public interface IPrintService
{
    Task PrintDocumentVenteAsync(DocumentVenteDetailDto document, ParametresDto parametres, ICurrencyService devise);
    Task PrintDocumentAchatAsync(DocumentAchatDetailDto document, ParametresDto parametres, ICurrencyService devise);
    Task PrintReglementAsync(ReglementDto reglement, ParametresDto parametres, ICurrencyService devise);
    Task PrintAcompteAsync(AcompteDto acompte, ParametresDto parametres, ICurrencyService devise);
}

public class PrintService : IPrintService
{
    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;

    public PrintService(IJSRuntime js)
    {
        _js = js;
    }

    public Task PrintDocumentVenteAsync(DocumentVenteDetailDto document, ParametresDto parametres, ICurrencyService devise)
        => PrintAsync(
            $"Document {document.Numero}",
            BuildDocumentHtml(
                parametres,
                devise,
                NormalizeFormat(parametres.FormatImpressionDocuments),
                NormalizeDocumentPaperFormat(parametres.FormatPapierDocuments),
                document.TypeLibelle,
                document.Numero,
                document.StatutLibelle,
                "Client",
                document.ClientNom,
                "Date",
                document.DateDocument,
                "Echeance",
                document.DateEcheance,
                document.MontantHT,
                document.MontantTVA,
                document.MontantTTC,
                document.FraisLivraison,
                document.MontantAcompte,
                document.NotesExterne,
                document.Lignes.Select(l => new PrintLine(
                    l.ArticleCode ?? string.Empty,
                    l.Designation,
                    l.Quantite,
                    l.PrixUnitaireHT,
                    l.MontantTTC)).ToList()),
            NormalizeDocumentPaperFormat(parametres.FormatPapierDocuments),
            parametres.ImprimanteDocumentsDefaut);

    public Task PrintDocumentAchatAsync(DocumentAchatDetailDto document, ParametresDto parametres, ICurrencyService devise)
        => PrintAsync(
            $"Document {document.Numero}",
            BuildDocumentHtml(
                parametres,
                devise,
                NormalizeFormat(parametres.FormatImpressionDocuments),
                NormalizeDocumentPaperFormat(parametres.FormatPapierDocuments),
                document.TypeLibelle,
                document.Numero,
                document.StatutLibelle,
                "Fournisseur",
                document.FournisseurNom,
                "Date",
                document.DateDocument,
                "Livraison prevue",
                document.DateLivraisonPrevue,
                document.MontantHT,
                document.MontantTVA,
                document.MontantTTC,
                document.FraisLivraison,
                0m,
                document.NotesInternes,
                document.Lignes.Select(l => new PrintLine(
                    l.ArticleCode ?? string.Empty,
                    l.Designation,
                    l.Quantite,
                    l.PrixUnitaireHT,
                    l.MontantTTC)).ToList()),
            NormalizeDocumentPaperFormat(parametres.FormatPapierDocuments),
            parametres.ImprimanteDocumentsDefaut);

    public Task PrintReglementAsync(ReglementDto reglement, ParametresDto parametres, ICurrencyService devise)
        => PrintAsync(
            $"Recu {reglement.NumeroDoc ?? reglement.Id.ToString()}",
            BuildReceiptHtml(
                parametres,
                devise,
                NormalizeFormat(parametres.FormatImpressionRecus),
                NormalizeReceiptPaperFormat(parametres.FormatPapierRecus),
                "Recu de reglement",
                reglement.TiersNom ?? "Tiers",
                reglement.NumeroDoc,
                reglement.DateReglement,
                reglement.ModeLibelle,
                reglement.Montant,
                reglement.Reference,
                reglement.Notes),
            NormalizeReceiptPaperFormat(parametres.FormatPapierRecus),
            parametres.ImprimanteRecusDefaut);

    public Task PrintAcompteAsync(AcompteDto acompte, ParametresDto parametres, ICurrencyService devise)
        => PrintAsync(
            $"Acompte {acompte.NumeroDoc ?? acompte.Id.ToString()}",
            BuildReceiptHtml(
                parametres,
                devise,
                NormalizeFormat(parametres.FormatImpressionRecus),
                NormalizeReceiptPaperFormat(parametres.FormatPapierRecus),
                acompte.ClientId.HasValue ? "Recu d'acompte client" : "Recu d'acompte fournisseur",
                acompte.ClientNom ?? acompte.FournisseurNom ?? "Tiers",
                acompte.NumeroDoc,
                acompte.DateAcompte,
                acompte.ModeLibelle,
                acompte.Montant,
                acompte.Reference,
                acompte.Notes,
                acompte.MontantDisponible,
                acompte.MontantUtilise),
            NormalizeReceiptPaperFormat(parametres.FormatPapierRecus),
            parametres.ImprimanteRecusDefaut);

    private async Task PrintAsync(string title, string htmlBody, string pageFormat, string? preferredPrinter)
    {
        var html = $"""
<!DOCTYPE html>
<html lang="fr">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>{Html(title)}</title>
    <style>{BuildBaseCss()}</style>
</head>
<body data-preferred-printer="{HtmlAttribute(preferredPrinter ?? string.Empty)}" class="paper-host paper-{pageFormat.ToLowerInvariant().Replace("_", "-")}">{htmlBody}</body>
</html>
""";

        _module ??= await _js.InvokeAsync<IJSObjectReference>("import", "./js/printing.js?v=20260325-3");
        await _module.InvokeVoidAsync("printDocument", title, html);
    }

    private string BuildDocumentHtml(
        ParametresDto p,
        ICurrencyService devise,
        string format,
        string paperFormat,
        string typeLibelle,
        string numero,
        string statut,
        string tiersLabel,
        string tiersNom,
        string dateLabel,
        DateTime dateDocument,
        string date2Label,
        DateTime? dateSecondaire,
        decimal montantHt,
        decimal montantTva,
        decimal montantTtc,
        decimal fraisLivraison,
        decimal acompte,
        string? notes,
        List<PrintLine> lignes)
    {
        var sb = new StringBuilder();
        sb.Append($"<div class='print-sheet paper-{paperFormat.ToLowerInvariant().Replace("_", "-")} format-{format.ToLowerInvariant()}'>");
        sb.Append(BuildHeader(p, typeLibelle, numero));
        sb.Append("<section class='print-meta-grid'>");
        sb.Append(BuildMetaItem("Statut", statut));
        sb.Append(BuildMetaItem(tiersLabel, tiersNom));
        sb.Append(BuildMetaItem(dateLabel, dateDocument.ToString("dd/MM/yyyy")));
        sb.Append(BuildMetaItem(date2Label, dateSecondaire?.ToString("dd/MM/yyyy") ?? "-"));
        sb.Append("</section>");

        sb.Append("<table class='print-table'><thead><tr>");
        sb.Append("<th>Article</th><th>Designation</th><th class='text-right'>Qte</th><th class='text-right'>PU HT</th><th class='text-right'>Montant TTC</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (var ligne in lignes)
        {
            sb.Append("<tr>");
            sb.Append($"<td>{Html(ligne.Code)}</td>");
            sb.Append($"<td>{Html(ligne.Designation)}</td>");
            sb.Append($"<td class='text-right'>{ligne.Quantite.ToString($"N{devise.QuantityDecimals}")}</td>");
            sb.Append($"<td class='text-right'>{Html(devise.FormatAmount(ligne.PrixUnitaireHt))}</td>");
            sb.Append($"<td class='text-right'>{Html(devise.FormatAmount(ligne.MontantTtc))}</td>");
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");

        sb.Append("<section class='print-totals'>");
        sb.Append(BuildTotalLine("Montant HT", devise.FormatAmount(montantHt)));
        sb.Append(BuildTotalLine("TVA", devise.FormatAmount(montantTva)));
        if (fraisLivraison > 0)
        {
            sb.Append(BuildTotalLine("Frais de livraison", devise.FormatAmount(fraisLivraison)));
        }
        if (acompte > 0)
        {
            sb.Append(BuildTotalLine("Acompte", devise.FormatAmount(acompte)));
        }
        sb.Append(BuildTotalLine("Total TTC", devise.FormatAmount(montantTtc), true));
        sb.Append("</section>");

        if (!string.IsNullOrWhiteSpace(notes))
        {
            sb.Append($"<section class='print-notes'><h3>Notes</h3><p>{Html(notes)}</p></section>");
        }

        sb.Append(BuildFooter(p));
        sb.Append("</div>");
        return sb.ToString();
    }

    private string BuildReceiptHtml(
        ParametresDto p,
        ICurrencyService devise,
        string format,
        string paperFormat,
        string titre,
        string tiersNom,
        string? numeroDoc,
        DateTime date,
        string modeReglement,
        decimal montant,
        string? reference,
        string? notes,
        decimal? disponible = null,
        decimal? utilise = null)
    {
        var sb = new StringBuilder();
        if (paperFormat == "2X_A5_A4")
        {
            sb.Append("<div class='print-sheet receipt-sheet receipt-double paper-a4'>");
            sb.Append("<div class='receipt-duplicate'>");
            sb.Append(BuildReceiptCardHtml(p, devise, format, titre, tiersNom, numeroDoc, date, modeReglement, montant, reference, notes, disponible, utilise));
            sb.Append("</div>");
            sb.Append("<div class='receipt-divider'></div>");
            sb.Append("<div class='receipt-duplicate'>");
            sb.Append(BuildReceiptCardHtml(p, devise, format, titre, tiersNom, numeroDoc, date, modeReglement, montant, reference, notes, disponible, utilise));
            sb.Append("</div>");
            sb.Append("</div>");
        }
        else
        {
            sb.Append($"<div class='print-sheet receipt-sheet paper-{paperFormat.ToLowerInvariant().Replace("_", "-")} format-{format.ToLowerInvariant()}'>");
            sb.Append(BuildReceiptCardHtml(p, devise, format, titre, tiersNom, numeroDoc, date, modeReglement, montant, reference, notes, disponible, utilise));
            sb.Append("</div>");
        }
        return sb.ToString();
    }

    private string BuildReceiptCardHtml(
        ParametresDto p,
        ICurrencyService devise,
        string format,
        string titre,
        string tiersNom,
        string? numeroDoc,
        DateTime date,
        string modeReglement,
        decimal montant,
        string? reference,
        string? notes,
        decimal? disponible,
        decimal? utilise)
    {
        var sb = new StringBuilder();
        sb.Append($"<div class='receipt-card format-{format.ToLowerInvariant()}'>");
        sb.Append(BuildHeader(p, titre, numeroDoc ?? "Sans document"));
        sb.Append("<section class='print-meta-grid'>");
        sb.Append(BuildMetaItem("Tiers", tiersNom));
        sb.Append(BuildMetaItem("Date", date.ToString("dd/MM/yyyy")));
        sb.Append(BuildMetaItem("Mode", modeReglement));
        sb.Append(BuildMetaItem("Reference", string.IsNullOrWhiteSpace(reference) ? "-" : reference));
        sb.Append("</section>");
        sb.Append("<section class='receipt-highlight'>");
        sb.Append("<span>Montant recu</span>");
        sb.Append($"<strong>{Html(devise.FormatAmount(montant))}</strong>");
        sb.Append("</section>");
        if (disponible.HasValue || utilise.HasValue)
        {
            sb.Append("<section class='print-meta-grid'>");
            if (utilise.HasValue)
            {
                sb.Append(BuildMetaItem("Montant utilise", devise.FormatAmount(utilise.Value)));
            }
            if (disponible.HasValue)
            {
                sb.Append(BuildMetaItem("Montant disponible", devise.FormatAmount(disponible.Value)));
            }
            sb.Append("</section>");
        }
        if (!string.IsNullOrWhiteSpace(notes))
        {
            sb.Append($"<section class='print-notes'><h3>Observation</h3><p>{Html(notes)}</p></section>");
        }
        sb.Append(BuildFooter(p));
        sb.Append("</div>");
        return sb.ToString();
    }

    private string BuildHeader(ParametresDto p, string titre, string numero)
    {
        var logo = string.IsNullOrWhiteSpace(p.LogoEntreprise)
            ? string.Empty
            : $"<div class='company-logo-wrap'><img class='company-logo' src='{HtmlAttribute(p.LogoEntreprise)}' alt='Logo entreprise' /></div>";

        var societe = new StringBuilder();
        societe.Append($"<strong>{Html(p.RaisonSociale)}</strong>");
        if (!string.IsNullOrWhiteSpace(p.FormeJuridique))
        {
            societe.Append($"<span>{Html(p.FormeJuridique)}</span>");
        }
        if (!string.IsNullOrWhiteSpace(p.Adresse) || !string.IsNullOrWhiteSpace(p.CodePostal) || !string.IsNullOrWhiteSpace(p.Ville))
        {
            societe.Append($"<span>{Html($"{p.Adresse} {p.CodePostal} {p.Ville}".Trim())}</span>");
        }
        if (!string.IsNullOrWhiteSpace(p.Telephone) || !string.IsNullOrWhiteSpace(p.Email))
        {
            societe.Append($"<span>{Html($"{p.Telephone}  {p.Email}".Trim())}</span>");
        }
        if (!string.IsNullOrWhiteSpace(p.SiteWeb))
        {
            societe.Append($"<span>{Html(p.SiteWeb)}</span>");
        }

        return $"""
<header class="print-header">
    <div class="company-box">
        {logo}
        <div class="company-text">{societe}</div>
    </div>
    <div class="document-box">
        <span class="document-kicker">{Html(titre)}</span>
        <h1>{Html(numero)}</h1>
    </div>
</header>
""";
    }

    private string BuildFooter(ParametresDto p)
    {
        var mentions = new List<string>();
        if (!string.IsNullOrWhiteSpace(p.Siret)) mentions.Add($"SIRET {Html(p.Siret)}");
        if (!string.IsNullOrWhiteSpace(p.NumTVA)) mentions.Add($"TVA {Html(p.NumTVA)}");
        if (!string.IsNullOrWhiteSpace(p.Banque)) mentions.Add($"Banque {Html(p.Banque)}");
        if (!string.IsNullOrWhiteSpace(p.Iban)) mentions.Add($"IBAN {Html(p.Iban)}");
        if (!string.IsNullOrWhiteSpace(p.Bic)) mentions.Add($"BIC {Html(p.Bic)}");

        return $"<footer class='print-footer'>{string.Join(" <span class='dot-sep'>•</span> ", mentions)}</footer>";
    }

    private static string BuildMetaItem(string label, string value)
        => $"<div class='print-meta-item'><span>{Html(label)}</span><strong>{Html(value)}</strong></div>";

    private static string BuildTotalLine(string label, string value, bool accent = false)
        => $"<div class='print-total{(accent ? " print-total--accent" : string.Empty)}'><span>{Html(label)}</span><strong>{Html(value)}</strong></div>";

    private static string NormalizeFormat(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "PROFESSIONNEL" => "PROFESSIONNEL",
        "COMPACT" => "COMPACT",
        _ => "STANDARD"
    };

    private static string NormalizeDocumentPaperFormat(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "80MM" => "80MM",
        "A5" => "A5",
        _ => "A4"
    };

    private static string NormalizeReceiptPaperFormat(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "A4" => "A4",
        "80MM" => "80MM",
        "2X_A5_A4" => "2X_A5_A4",
        _ => "A5"
    };

    private static string BuildBaseCss() => """
        @page sheet-a4 { size: A4 portrait; margin: 0; }
        @page sheet-a5 { size: A5 portrait; margin: 0; }
        @page sheet-80mm { size: 80mm auto; margin: 0; }
        @page sheet-double { size: A4 portrait; margin: 0; }
        :root {
            --print-text: #16324b;
            --print-muted: #60758c;
            --print-line: #d8e1eb;
            --print-soft: #f4f7fb;
            --print-accent: #2b6cb0;
            --print-accent-soft: #dbeafe;
        }
        * { box-sizing: border-box; }
        body {
            margin: 0;
            background: #eef3f8;
            color: var(--print-text);
            font-family: "Segoe UI", Tahoma, sans-serif;
        }
        .paper-a4 { page: sheet-a4; }
        .paper-a5 { page: sheet-a5; }
        .paper-80mm { page: sheet-80mm; }
        .paper-2x-a5-a4 { page: sheet-double; }
        .print-sheet {
            width: 210mm;
            min-height: 297mm;
            margin: 0 auto;
            background: #fff;
            padding: 18mm 16mm;
        }
        .print-sheet.paper-a5 {
            width: 148mm;
            min-height: 210mm;
            padding: 12mm 11mm;
        }
        .print-sheet.paper-80mm {
            width: 80mm;
            min-height: 160mm;
            padding: 8mm 6mm;
        }
        .print-sheet.format-compact { padding: 12mm 12mm; }
        .print-sheet.paper-a5.format-compact { padding: 10mm 9mm; }
        .print-sheet.paper-80mm.format-compact { padding: 6mm 5mm; }
        .print-sheet.format-professionnel .document-box {
            background: linear-gradient(135deg, #16324b, #2b6cb0);
            color: #fff;
        }
        .receipt-card.format-professionnel .document-box {
            background: linear-gradient(135deg, #16324b, #2b6cb0);
            color: #fff;
        }
        .receipt-double {
            display: flex;
            flex-direction: column;
            gap: 10mm;
        }
        .receipt-duplicate {
            flex: 1;
            min-height: calc((297mm - 46mm) / 2);
            border: 1px dashed #b8c6d8;
            border-radius: 12px;
            padding: 8mm 9mm;
        }
        .receipt-divider {
            border-top: 2px dashed #9bb2c9;
        }
        .receipt-card {
            height: 100%;
        }
        .print-header {
            display: flex;
            justify-content: space-between;
            gap: 18px;
            align-items: flex-start;
            margin-bottom: 22px;
        }
        .print-sheet.paper-80mm .print-header,
        .receipt-card .print-header {
            gap: 10px;
        }
        .company-box {
            display: flex;
            gap: 14px;
            align-items: flex-start;
            flex: 1;
        }
        .print-sheet.paper-80mm .company-box,
        .receipt-card .company-box {
            gap: 8px;
        }
        .company-logo-wrap {
            width: 84px;
            min-width: 84px;
            height: 84px;
            border: 1px solid var(--print-line);
            border-radius: 16px;
            display: flex;
            align-items: center;
            justify-content: center;
            overflow: hidden;
            background: #fff;
        }
        .company-logo {
            max-width: 100%;
            max-height: 100%;
            object-fit: contain;
        }
        .company-text {
            display: flex;
            flex-direction: column;
            gap: 4px;
            font-size: 12px;
            color: var(--print-muted);
        }
        .company-text strong {
            color: var(--print-text);
            font-size: 20px;
        }
        .print-sheet.paper-80mm .company-text,
        .receipt-card .company-text {
            font-size: 10px;
        }
        .print-sheet.paper-80mm .company-text strong,
        .receipt-card .company-text strong {
            font-size: 14px;
        }
        .document-box {
            min-width: 250px;
            border: 1px solid var(--print-line);
            border-radius: 18px;
            padding: 18px 20px;
            background: var(--print-soft);
            text-align: right;
        }
        .print-sheet.paper-80mm .document-box,
        .receipt-card .document-box {
            min-width: 0;
            padding: 10px 12px;
            border-radius: 12px;
        }
        .document-kicker {
            display: inline-block;
            font-size: 11px;
            text-transform: uppercase;
            letter-spacing: .16em;
            color: var(--print-muted);
            margin-bottom: 6px;
        }
        .document-box h1 {
            margin: 0;
            font-size: 28px;
        }
        .print-sheet.paper-80mm .document-box h1,
        .receipt-card .document-box h1 {
            font-size: 18px;
        }
        .print-meta-grid {
            display: grid;
            grid-template-columns: repeat(4, minmax(0, 1fr));
            gap: 12px;
            margin-bottom: 18px;
        }
        .print-sheet.paper-80mm .print-meta-grid,
        .receipt-card .print-meta-grid {
            grid-template-columns: repeat(2, minmax(0, 1fr));
            gap: 8px;
        }
        .print-meta-item {
            border: 1px solid var(--print-line);
            border-radius: 14px;
            padding: 12px;
            background: #fff;
        }
        .print-meta-item span {
            display: block;
            font-size: 11px;
            text-transform: uppercase;
            color: var(--print-muted);
            margin-bottom: 6px;
            letter-spacing: .08em;
        }
        .print-meta-item strong {
            font-size: 14px;
        }
        .print-table {
            width: 100%;
            border-collapse: collapse;
            margin-top: 10px;
        }
        .print-table thead th {
            background: var(--print-soft);
            color: var(--print-muted);
            font-size: 11px;
            text-transform: uppercase;
            letter-spacing: .08em;
            border-bottom: 1px solid var(--print-line);
            padding: 11px 10px;
            text-align: left;
        }
        .print-table td {
            padding: 10px;
            border-bottom: 1px solid var(--print-line);
            font-size: 13px;
        }
        .print-sheet.paper-80mm .print-table thead th,
        .print-sheet.paper-80mm .print-table td {
            padding: 6px 4px;
            font-size: 10px;
        }
        .text-right { text-align: right; }
        .print-totals {
            margin-left: auto;
            margin-top: 18px;
            width: 320px;
            border: 1px solid var(--print-line);
            border-radius: 16px;
            padding: 10px 14px;
            background: #fbfdff;
        }
        .print-sheet.paper-80mm .print-totals {
            width: 100%;
        }
        .print-total {
            display: flex;
            justify-content: space-between;
            gap: 12px;
            padding: 8px 0;
            color: var(--print-muted);
        }
        .print-total strong {
            color: var(--print-text);
        }
        .print-total--accent {
            margin-top: 6px;
            padding-top: 12px;
            border-top: 1px solid var(--print-line);
            font-size: 15px;
        }
        .print-total--accent strong {
            color: var(--print-accent);
            font-size: 18px;
        }
        .receipt-highlight {
            margin: 22px 0;
            border-radius: 20px;
            background: linear-gradient(135deg, #eff6ff, #dbeafe);
            padding: 18px 22px;
            display: flex;
            justify-content: space-between;
            align-items: center;
            gap: 12px;
        }
        .receipt-highlight span {
            text-transform: uppercase;
            font-size: 12px;
            letter-spacing: .1em;
            color: var(--print-muted);
        }
        .receipt-highlight strong {
            font-size: 28px;
            color: var(--print-accent);
        }
        .print-sheet.paper-80mm .receipt-highlight,
        .receipt-card .receipt-highlight {
            padding: 12px 14px;
        }
        .print-sheet.paper-80mm .receipt-highlight strong,
        .receipt-card .receipt-highlight strong {
            font-size: 20px;
        }
        .print-notes {
            margin-top: 18px;
            border: 1px solid var(--print-line);
            border-radius: 16px;
            padding: 14px;
            background: #fff;
        }
        .print-notes h3 {
            margin: 0 0 10px;
            font-size: 13px;
            text-transform: uppercase;
            color: var(--print-muted);
            letter-spacing: .08em;
        }
        .print-notes p {
            margin: 0;
            white-space: pre-wrap;
            line-height: 1.5;
        }
        .print-footer {
            margin-top: 28px;
            padding-top: 12px;
            border-top: 1px solid var(--print-line);
            color: var(--print-muted);
            font-size: 11px;
        }
        .dot-sep { margin: 0 8px; color: #b4c4d6; }
        @media print {
            body { background: #fff; }
            .print-sheet { margin: 0; width: auto; min-height: auto; }
        }
        """;

    private static string Html(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
    private static string HtmlAttribute(string? value) => Html(value);

    private sealed record PrintLine(string Code, string Designation, decimal Quantite, decimal PrixUnitaireHt, decimal MontantTtc);
}
