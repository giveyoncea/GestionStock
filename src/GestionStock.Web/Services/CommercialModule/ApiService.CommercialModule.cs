using System.Net.Http.Json;
using GestionStock.Web.Models;

namespace GestionStock.Web.Services;

public partial class ApiService
{
    public Task<List<ClientDto>?> GetClientsAsync(string? q = null)
        => GetAsync<List<ClientDto>>($"api/commercial/clients{(q != null ? $"?q={Uri.EscapeDataString(q)}" : "")}");

    public Task<ResultDto?> CreerClientAsync(ClientRequest dto)
        => PostAsync("api/commercial/clients", dto);

    public Task<ResultDto?> ModifierClientAsync(Guid id, ClientRequest dto)
        => PutAsync($"api/commercial/clients/{id}", dto);

    public Task<List<DocumentVenteDto>?> GetVentesAsync(int? type = null, int? statut = null, Guid? clientId = null, string? q = null)
    {
        var qs = new List<string>();
        if (type.HasValue)
            qs.Add($"type={type}");
        if (statut.HasValue)
            qs.Add($"statut={statut}");
        if (clientId.HasValue)
            qs.Add($"clientId={clientId}");
        if (q != null)
            qs.Add($"q={Uri.EscapeDataString(q)}");
        var url = "api/commercial/ventes" + (qs.Any() ? "?" + string.Join("&", qs) : "");
        return GetAsync<List<DocumentVenteDto>>(url);
    }

    public async Task<DocumentVenteDetailDto?> GetVenteAsync(Guid id)
    {
        var document = await GetDocumentDetailAsync<DocumentVenteDetailDto>($"api/commercial/ventes/{id}");
        if (document == null)
            return null;

        document.Lignes ??= new();
        return document;
    }

    public Task<ResultDto?> CreerVenteAsync(DocumentVenteRequest dto)
        => PostAsync("api/commercial/ventes", dto);

    public Task<ResultDto?> ModifierVenteAsync(Guid id, DocumentVenteRequest dto)
        => PutAsync($"api/commercial/ventes/{id}", dto);

    public Task<ResultDto?> ValiderVenteAsync(Guid id)
        => PostEmptyAsync($"api/commercial/ventes/{id}/valider");

    public Task<ResultDto?> ComptabiliserVenteAsync(Guid id)
        => PostEmptyAsync($"api/commercial/ventes/{id}/comptabiliser");

    public Task<ResultDto?> AnnulerVenteAsync(Guid id)
        => PostEmptyAsync($"api/commercial/ventes/{id}/annuler");

    public async Task<ResultDto?> TransformerVenteAsync(Guid id, int typeDoc)
    {
        try
        {
            await SetAuthHeaderAsync();
            var response = await _http.PostAsync($"api/commercial/ventes/{id}/transformer/{typeDoc}", null);
            return await ParseResultAsync(response);
        }
        catch (Exception ex)
        {
            return new ResultDto(false, ex.Message, null);
        }
    }

    public async Task<ResultDto?> AjouterReglementVenteAsync(Guid id, ReglementRequest dto)
    {
        try
        {
            await SetAuthHeaderAsync();
            var response = await _http.PostAsJsonAsync($"api/commercial/ventes/{id}/reglement", dto);
            return await ParseResultAsync(response);
        }
        catch (Exception ex)
        {
            return new ResultDto(false, ex.Message, null);
        }
    }

    public Task<List<DocumentAchatDto>?> GetAchatsCommAsync(int? type = null)
        => GetAsync<List<DocumentAchatDto>>($"api/commercial/achats{(type.HasValue ? $"?type={type}" : "")}");

    public async Task<DocumentAchatDetailDto?> GetAchatCommAsync(Guid id)
    {
        var document = await GetDocumentDetailAsync<DocumentAchatDetailDto>($"api/commercial/achats/{id}");
        if (document == null)
            return null;

        document.Lignes ??= new();
        return document;
    }

    public Task<ResultDto?> CreerAchatCommAsync(DocumentAchatRequest dto)
        => PostAsync("api/commercial/achats", dto);

    public Task<ResultDto?> ModifierAchatCommAsync(Guid id, DocumentAchatRequest dto)
        => PutAsync($"api/commercial/achats/{id}", dto);

    public async Task<ResultDto?> TransformerAchatCommAsync(Guid id, int typeDoc)
    {
        try
        {
            await SetAuthHeaderAsync();
            var response = await _http.PostAsync($"api/commercial/achats/{id}/transformer/{typeDoc}", null);
            return await ParseResultAsync(response);
        }
        catch (Exception ex)
        {
            return new ResultDto(false, ex.Message, null);
        }
    }

    public Task<List<ReglementDto>?> GetReglementsAsync(Guid? documentId = null, int? typeDocument = null)
    {
        var qs = new List<string>();
        if (documentId.HasValue)
            qs.Add($"documentId={documentId}");
        if (typeDocument.HasValue)
            qs.Add($"typeDocument={typeDocument}");
        return GetAsync<List<ReglementDto>>("api/commercial/reglements" + (qs.Any() ? "?" + string.Join("&", qs) : ""));
    }

    public Task<ResultDto?> SupprimerReglementAsync(Guid id)
        => DeleteAsync($"api/commercial/reglements/{id}");

    public Task<List<AcompteDto>?> GetAcomptesAsync(Guid? clientId = null, Guid? fournisseurId = null, bool nonUtilisesSeulement = false)
    {
        var qs = new List<string>();
        if (clientId.HasValue)
            qs.Add($"clientId={clientId}");
        if (fournisseurId.HasValue)
            qs.Add($"fournisseurId={fournisseurId}");
        if (nonUtilisesSeulement)
            qs.Add("nonUtilisesSeulement=true");
        return GetAsync<List<AcompteDto>>("api/commercial/acomptes" + (qs.Any() ? "?" + string.Join("&", qs) : ""));
    }

    public Task<ResultDto?> CreerAcompteAsync(AcompteRequest dto)
        => PostAsync("api/commercial/acomptes", dto);

    public async Task<ResultDto?> ImputerAcompteAsync(Guid acompteId, Guid documentId, decimal montant)
    {
        try
        {
            await SetAuthHeaderAsync();
            var response = await _http.PostAsJsonAsync($"api/commercial/acomptes/{acompteId}/imputer/{documentId}", new { Montant = montant });
            return await ParseResultAsync(response);
        }
        catch (Exception ex)
        {
            return new ResultDto(false, ex.Message, null);
        }
    }

    public Task<CommercialDashboardDto?> GetCommercialDashboardAsync()
        => GetAsync<CommercialDashboardDto>("api/commercial/dashboard");

    public Task<ComptabiliteDto?> GetComptabiliteAsync()
        => GetAsync<ComptabiliteDto>("api/parametres/comptabilite");

    public async Task<ResultDto?> SauvegarderComptabiliteAsync(ComptabiliteDto dto)
    {
        try
        {
            await SetAuthHeaderAsync();
            var response = await _http.PostAsJsonAsync("api/parametres/comptabilite", dto);
            return await ParseResultAsync(response);
        }
        catch (Exception ex)
        {
            return new ResultDto(false, ex.Message, null);
        }
    }
}
