using System.Net.Http.Json;
using GestionStock.Web.Models;

namespace GestionStock.Web.Services;

public partial class ApiService
{
    public Task<DashboardDto?> GetDashboardAsync()
        => GetAsync<DashboardDto>("api/dashboard");

    public Task<PagedResult<ArticleDto>?> GetArticlesAsync(int page, int pageSize, string? search, string? categorie)
    {
        var url = $"api/articles?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(search))
            url += $"&search={Uri.EscapeDataString(search)}";
        if (!string.IsNullOrWhiteSpace(categorie))
            url += $"&categorie={Uri.EscapeDataString(categorie)}";
        return GetAsync<PagedResult<ArticleDto>>(url);
    }

    public Task<ArticleDto?> GetArticleAsync(Guid id)
        => GetAsync<ArticleDto>($"api/articles/{id}");

    public Task<IEnumerable<AlerteStockDto>?> GetArticlesEnAlerteAsync()
        => GetAsync<IEnumerable<AlerteStockDto>>("api/articles/alertes");

    public Task<ResultDto?> CreerArticleAsync(CreerArticleDto dto)
        => PostAsync("api/articles", dto);

    public Task<ResultDto?> ModifierArticleAsync(Guid id, ModifierArticleDto dto)
        => PutAsync($"api/articles/{id}", dto);

    public Task<ResultDto?> DesactiverArticleAsync(Guid id)
        => DeleteAsync($"api/articles/{id}");

    public Task<IEnumerable<StockResumeDto>?> GetStocksResumeAsync()
        => GetAsync<IEnumerable<StockResumeDto>>("api/stocks");

    public Task<IEnumerable<MouvementStockDto>?> GetMouvementsAsync(Guid? articleId, DateTime? du, DateTime? au)
    {
        var url = "api/stocks/mouvements?placeholder=1";
        if (articleId.HasValue)
            url += $"&articleId={articleId}";
        if (du.HasValue)
            url += $"&du={du:O}";
        if (au.HasValue)
            url += $"&au={au:O}";
        return GetAsync<IEnumerable<MouvementStockDto>>(url);
    }

    public Task<IEnumerable<DocumentStockDto>?> GetDocumentsStockAsync(DateTime? du, DateTime? au, int? type, string? q)
    {
        var url = "api/stocks/documents?placeholder=1";
        if (du.HasValue)
            url += $"&du={du:O}";
        if (au.HasValue)
            url += $"&au={au:O}";
        if (type.HasValue && type.Value > 0)
            url += $"&type={type.Value}";
        if (!string.IsNullOrWhiteSpace(q))
            url += $"&q={Uri.EscapeDataString(q)}";
        return GetAsync<IEnumerable<DocumentStockDto>>(url);
    }

    public Task<DocumentStockDto?> GetDocumentStockAsync(Guid id)
        => GetAsync<DocumentStockDto>($"api/stocks/documents/{id}");

    public Task<ResultDto?> ValiderDocumentStockAsync(Guid id)
        => PostEmptyAsync($"api/stocks/documents/{id}/valider");

    public Task<ResultDto?> CreerDocumentEntreeStockAsync(DocumentEntreeStockRequest dto)
        => PostAsync("api/stocks/documents/entree", dto);

    public Task<ResultDto?> CreerDocumentSortieStockAsync(DocumentSortieStockRequest dto)
        => PostAsync("api/stocks/documents/sortie", dto);

    public Task<ResultDto?> CreerDocumentTransfertStockAsync(DocumentTransfertStockRequest dto)
        => PostAsync("api/stocks/documents/transfert", dto);

    public Task<ResultDto?> EntreeStockAsync(EntreeStockDto dto)
        => PostAsync("api/stocks/entree", dto);

    public Task<ResultDto?> SortieStockAsync(SortieStockDto dto)
        => PostAsync("api/stocks/sortie", dto);

    public Task<PagedResult<FournisseurDto>?> GetFournisseursAsync(int page, int pageSize, string? search)
    {
        var url = $"api/fournisseurs?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(search))
            url += $"&search={Uri.EscapeDataString(search)}";
        return GetAsync<PagedResult<FournisseurDto>>(url);
    }

    public Task<ResultDto?> CreerFournisseurAsync(CreerFournisseurDto dto)
        => PostAsync("api/fournisseurs", dto);

    public Task<ResultDto?> ModifierFournisseurAsync(Guid id, CreerFournisseurDto dto)
        => PutAsync($"api/fournisseurs/{id}", dto);

    public Task<PagedResult<CommandeAchatDto>?> GetCommandesAsync(int page, int pageSize)
        => GetAsync<PagedResult<CommandeAchatDto>>($"api/commandes?page={page}&pageSize={pageSize}");

    public Task<IEnumerable<CommandeAchatDto>?> GetCommandesEnAttenteAsync()
        => GetAsync<IEnumerable<CommandeAchatDto>>("api/commandes/en-attente");

    public Task<CommandeAchatDto?> GetCommandeAsync(Guid id)
        => GetAsync<CommandeAchatDto>($"api/commandes/{id}");

    public Task<ResultDto?> CreerCommandeAsync(CreerCommandeDto dto)
        => PostAsync("api/commandes", dto);

    public Task<ResultDto?> ModifierCommandeAsync(Guid id, CreerCommandeDto dto)
        => PutAsync($"api/commandes/{id}", dto);

    public Task<ResultDto?> ValiderCommandeAsync(Guid id)
        => PostEmptyAsync($"api/commandes/{id}/valider");

    public Task<ResultDto?> ComptabiliserCommandeAsync(Guid id)
        => PostEmptyAsync($"api/commandes/{id}/comptabiliser");

    public Task<ResultDto?> AnnulerCommandeAsync(Guid id, string motif)
        => PostAsync($"api/commandes/{id}/annuler", new { motif });

    public Task<ParametresDto?> GetParametresAsync()
        => GetAsync<ParametresDto>("api/parametres");

    public Task<ResultDto?> SauvegarderParametresAsync(ParametresDto dto)
        => PostAsync("api/parametres", dto);

    public async Task<InscriptionResultat?> InscrireAsync(InscriptionRequest dto)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/registration", dto);
            return await response.Content.ReadFromJsonAsync<InscriptionResultat>(JsonOpts);
        }
        catch (Exception ex)
        {
            return new InscriptionResultat { Succes = false, Message = ex.Message };
        }
    }

    public async Task<bool> EmailDisponibleAsync(string email)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<EmailDispo>($"api/registration/disponibilite?email={Uri.EscapeDataString(email)}");
            return result?.Disponible ?? true;
        }
        catch
        {
            return true;
        }
    }

    private record EmailDispo(bool Disponible);

    public Task<List<TenantDto>?> GetTenantsAsync()
        => GetAsync<List<TenantDto>>("api/admin/tenants");

    public Task<TenantDetailDto?> GetTenantDetailAsync(string code)
        => GetAsync<TenantDetailDto>($"api/admin/tenants/{code}");

    public async Task<ResultDto?> CreerTenantAsync(TenantCreateRequest dto)
    {
        try
        {
            await SetAuthHeaderAsync();
            var response = await _http.PostAsJsonAsync("api/admin/tenants", dto);
            return await response.Content.ReadFromJsonAsync<ResultDto>(JsonOpts);
        }
        catch (Exception ex)
        {
            return new ResultDto(false, ex.Message, null);
        }
    }

    public Task<ResultDto?> ActiverTenantAsync(string code)
        => PostEmptyAsync($"api/admin/tenants/{code}/activer");

    public Task<ResultDto?> SusprendreTenantAsync(string code)
        => PostEmptyAsync($"api/admin/tenants/{code}/suspendre");

    public Task<ResultDto?> SupprimerTenantAsync(string code)
        => DeleteAsync($"api/admin/tenants/{code}");

    public async Task<ResultDto?> ResetPasswordTenantAsync(string code, string motDePasse)
    {
        try
        {
            await SetAuthHeaderAsync();
            var response = await _http.PostAsJsonAsync($"api/admin/tenants/{code}/reset-password", new ResetPasswordRequest { NouveauMotDePasse = motDePasse });
            return await response.Content.ReadFromJsonAsync<ResultDto>(JsonOpts);
        }
        catch (Exception ex)
        {
            return new ResultDto(false, ex.Message, null);
        }
    }

    public Task<List<UtilisateurDto>?> GetUtilisateursAsync()
        => GetAsync<List<UtilisateurDto>>("api/utilisateurs");

    public Task<List<RoleDto>?> GetRolesAsync()
        => GetAsync<List<RoleDto>>("api/utilisateurs/roles");

    public Task<ResultDto?> CreerUtilisateurAsync(UtilisateurRequest dto)
        => PostAsync("api/utilisateurs", dto);

    public Task<ResultDto?> ModifierUtilisateurAsync(string id, ModifierUtilisateurRequest dto)
        => PutAsync($"api/utilisateurs/{id}", dto);

    public async Task<ResultDto?> ResetMdpUtilisateurAsync(string id, string mdp)
    {
        try
        {
            await SetAuthHeaderAsync();
            var response = await _http.PostAsJsonAsync($"api/utilisateurs/{id}/reset-password", new ResetMdpRequest { NouveauMotDePasse = mdp });
            return await ParseResultAsync(response);
        }
        catch (Exception ex)
        {
            return new ResultDto(false, ex.Message, null);
        }
    }

    public Task<ResultDto?> ActiverUtilisateurAsync(string id)
        => PostEmptyAsync($"api/utilisateurs/{id}/activer");

    public Task<ResultDto?> DesactiverUtilisateurAsync(string id)
        => PostEmptyAsync($"api/utilisateurs/{id}/desactiver");

    public Task<ResultDto?> DeverrouillerUtilisateurAsync(string id)
        => PostEmptyAsync($"api/utilisateurs/{id}/deverrouiller");

    public async Task<ResultDto?> ChangerMotDePasseAsync(string ancien, string nouveau)
    {
        try
        {
            await SetAuthHeaderAsync();
            var response = await _http.PostAsJsonAsync("api/utilisateurs/changer-mot-de-passe", new ChangerMdpRequest { AncienMotDePasse = ancien, NouveauMotDePasse = nouveau });
            return await ParseResultAsync(response);
        }
        catch (Exception ex)
        {
            return new ResultDto(false, ex.Message, null);
        }
    }

    public Task<List<RoleCompletDto>?> GetRolesCompletAsync()
        => GetAsync<List<RoleCompletDto>>("api/roles");

    public Task<ResultDto?> CreerRoleAsync(RoleRequest dto)
        => PostAsync("api/roles", dto);

    public Task<ResultDto?> ModifierRoleAsync(int id, RoleRequest dto)
        => PutAsync($"api/roles/{id}", dto);

    public Task<ResultDto?> ActiverRoleAsync(int id)
        => PostEmptyAsync($"api/roles/{id}/activer");

    public Task<ResultDto?> DesactiverRoleAsync(int id)
        => PostEmptyAsync($"api/roles/{id}/desactiver");

    public Task<ResultDto?> SupprimerRoleAsync(int id)
        => DeleteAsync($"api/roles/{id}");

    public Task<string[]?> GetCataloguePermissionsAsync()
        => GetAsync<string[]>("api/utilisateurs/permissions/catalogue");

    public Task<PermissionsDetailDto?> GetPermissionsRoleAsync(int roleId)
        => GetAsync<PermissionsDetailDto>($"api/utilisateurs/roles/{roleId}/permissions");

    public async Task<ResultDto?> SetPermissionsRoleAsync(int roleId, string[] permissions)
    {
        try
        {
            await SetAuthHeaderAsync();
            var response = await _http.PutAsJsonAsync($"api/utilisateurs/roles/{roleId}/permissions", new PermissionsRequest { Permissions = permissions });
            return await ParseResultAsync(response);
        }
        catch (Exception ex)
        {
            return new ResultDto(false, ex.Message, null);
        }
    }

    public Task<PermissionsDetailDto?> GetPermissionsUtilisateurAsync(string userId)
        => GetAsync<PermissionsDetailDto>($"api/utilisateurs/{userId}/permissions");

    public async Task<ResultDto?> SetPermissionsUtilisateurAsync(string userId, string[] permissions, bool reset = false)
    {
        try
        {
            await SetAuthHeaderAsync();
            var response = await _http.PutAsJsonAsync($"api/utilisateurs/{userId}/permissions", new PermissionsRequest { Permissions = permissions, ResetToRole = reset });
            return await ParseResultAsync(response);
        }
        catch (Exception ex)
        {
            return new ResultDto(false, ex.Message, null);
        }
    }

    public Task<List<LotTracabiliteDto>?> GetLotsAsync(Guid? articleId = null, bool alertePeremption = false)
    {
        var url = "api/tracabilite/lots";
        if (articleId.HasValue)
            url += $"?articleId={articleId}";
        if (alertePeremption)
            url += (url.Contains("?") ? "&" : "?") + "alertePeremption=true";
        return GetAsync<List<LotTracabiliteDto>>(url);
    }

    public Task<List<AlertePeremptionDto>?> GetAlertesPeremptionAsync(int joursAvance = 30)
        => GetAsync<List<AlertePeremptionDto>>($"api/tracabilite/alertes-peremption?joursAvance={joursAvance}");

    public Task<ResultDto?> CreerLotAsync(LotRequest dto)
        => PostAsync("api/tracabilite/lots", dto);

    public async Task<ResultDto?> ModifierStatutLotAsync(Guid lotId, int statut)
    {
        try
        {
            await SetAuthHeaderAsync();
            var response = await _http.PutAsJsonAsync($"api/tracabilite/lots/{lotId}/statut", new { Statut = statut });
            return await ParseResultAsync(response);
        }
        catch (Exception ex)
        {
            return new ResultDto(false, ex.Message, null);
        }
    }

    public Task<List<MouvementTracabiliteDto>?> GetMouvementsLotAsync(Guid lotId)
        => GetAsync<List<MouvementTracabiliteDto>>($"api/tracabilite/lots/{lotId}/mouvements");

    public Task<object?> GetFicheTracabiliteAsync(Guid articleId, DateTime? du, DateTime? au)
        => GetAsync<object>($"api/tracabilite/articles/{articleId}" + (du.HasValue ? $"?du={du:yyyy-MM-dd}&au={au:yyyy-MM-dd}" : ""));

    public Task<List<CategorieDto>?> GetCategoriesAsync(bool actifSeulement = true)
        => GetAsync<List<CategorieDto>>($"api/categories?actifSeulement={actifSeulement}");

    public Task<ResultDto?> CreerCategorieAsync(CategorieRequest dto)
        => PostAsync("api/categories", dto);

    public Task<ResultDto?> ModifierCategorieAsync(Guid id, CategorieRequest dto)
        => PutAsync($"api/categories/{id}", dto);

    public Task<ResultDto?> DesactiverCategorieAsync(Guid id)
        => DeleteAsync($"api/categories/{id}");

    public Task<List<EmplacementDto>?> GetEmplacementsAsync()
        => GetAsync<List<EmplacementDto>>("api/emplacements");

    public Task<ResultDto?> TransfertStockAsync(TransfertRequest dto)
        => PostAsync("api/stocks/transfert", dto);

    public Task<ResultDto?> AjustementStockAsync(AjustementRequest dto)
        => PostAsync("api/stocks/ajustement", dto);

    public Task<List<DepotDto>?> GetDepotsAsync(bool actifSeulement = true)
        => GetAsync<List<DepotDto>>($"api/depots?actifSeulement={actifSeulement}");

    public Task<ResultDto?> CreerDepotAsync(DepotRequest dto)
        => PostAsync("api/depots", dto);

    public Task<ResultDto?> ModifierDepotAsync(Guid id, DepotRequest dto)
        => PutAsync($"api/depots/{id}", dto);

    public Task<ResultDto?> DefinirDepotPrincipalAsync(Guid id)
        => PostEmptyAsync($"api/depots/{id}/principal");

    public Task<ResultDto?> DesactiverDepotAsync(Guid id)
        => DeleteAsync($"api/depots/{id}");

    public Task<List<FamilleArticleDto>?> GetFamillesAsync(bool actifSeulement = true)
        => GetAsync<List<FamilleArticleDto>>($"api/familles?actifSeulement={actifSeulement}");

    public Task<ResultDto?> CreerFamilleAsync(FamilleRequest dto)
        => PostAsync("api/familles", dto);

    public Task<ResultDto?> ModifierFamilleAsync(Guid id, FamilleRequest dto)
        => PutAsync($"api/familles/{id}", dto);

    public Task<ResultDto?> DesactiverFamilleAsync(Guid id)
        => DeleteAsync($"api/familles/{id}");
}


