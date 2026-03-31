using GestionStock.API.Services;
using GestionStock.Application.DTOs;
using GestionStock.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Security.Claims;
using System.Text.Json;

namespace GestionStock.API.Controllers;

[ApiController]
[Route("api/commercial")]
[Authorize]
public class CommercialController : ControllerBase
{
    private readonly ITenantService _tenant;
    private readonly IConfiguration _config;
    private readonly ICommercialClientService _commercialClientService;
    private readonly ICommercialVenteQueryService _commercialVenteQueryService;
    private readonly ICommercialVenteCommandService _commercialVenteCommandService;
    private readonly ICommercialAchatQueryService _commercialAchatQueryService;
    private readonly ICommercialAchatCommandService _commercialAchatCommandService;
    private string UserId => User.FindFirstValue("sub") ?? "system";
    private string ConnStr { get {
        var t = User.FindFirstValue("tenant");
        return !string.IsNullOrEmpty(t) ? _tenant.GetConnectionString(t)
            : _config.GetConnectionString("DefaultConnection")!;
    }}

    public CommercialController(
        ITenantService tenant,
        IConfiguration config,
        ICommercialClientService commercialClientService,
        ICommercialVenteQueryService commercialVenteQueryService,
        ICommercialVenteCommandService commercialVenteCommandService,
        ICommercialAchatQueryService commercialAchatQueryService,
        ICommercialAchatCommandService commercialAchatCommandService)
    {
        _tenant = tenant;
        _config = config;
        _commercialClientService = commercialClientService;
        _commercialVenteQueryService = commercialVenteQueryService;
        _commercialVenteCommandService = commercialVenteCommandService;
        _commercialAchatQueryService = commercialAchatQueryService;
        _commercialAchatCommandService = commercialAchatCommandService;
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // TABLE INIT
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    private async Task EnsureTablesAsync(SqlConnection conn)
    {
        var tables = new[]
        {
            @"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Clients' AND xtype='U')
              CREATE TABLE Clients (Id uniqueidentifier NOT NULL PRIMARY KEY,
                Code nvarchar(20) NOT NULL, RaisonSociale nvarchar(200) NOT NULL,
                TypeClient int NOT NULL DEFAULT 1, -- 1=Particulier 2=Professionnel
                Email nvarchar(256) NULL, Telephone nvarchar(30) NULL,
                Adresse nvarchar(300) NULL, CodePostal nvarchar(10) NULL,
                Ville nvarchar(100) NULL, Pays nvarchar(100) NOT NULL DEFAULT 'France',
                NumeroTVA nvarchar(30) NULL, Siret nvarchar(20) NULL,
                RepresentantId uniqueidentifier NULL,
                DelaiPaiementJours int NOT NULL DEFAULT 30,
                TauxRemise decimal(5,2) NOT NULL DEFAULT 0,
                PlafondCredit decimal(18,2) NOT NULL DEFAULT 0,
                LimiteDepassement bit NOT NULL DEFAULT 0,
                Notes nvarchar(1000) NULL,
                EstActif bit NOT NULL DEFAULT 1,
                CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
                UpdatedAt datetime2 NULL, CreatedBy nvarchar(450) NOT NULL DEFAULT '')",

            @"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Representants' AND xtype='U')
              CREATE TABLE Representants (Id uniqueidentifier NOT NULL PRIMARY KEY,
                Code nvarchar(20) NOT NULL, Nom nvarchar(200) NOT NULL,
                Email nvarchar(256) NULL, Telephone nvarchar(30) NULL,
                Zone nvarchar(100) NULL,
                TypeCommission int NOT NULL DEFAULT 1, -- 1=Fixe 2=Pourcentage 3=Marge
                TauxCommission decimal(5,2) NOT NULL DEFAULT 0,
                ObjectifMensuel decimal(18,2) NOT NULL DEFAULT 0,
                EstActif bit NOT NULL DEFAULT 1,
                CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
                CreatedBy nvarchar(450) NOT NULL DEFAULT '')",

            @"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='DocumentsVente' AND xtype='U')
              CREATE TABLE DocumentsVente (Id uniqueidentifier NOT NULL PRIMARY KEY,
                Numero nvarchar(30) NOT NULL, TypeDocument int NOT NULL,
                -- TypeDocument: 1=Devis 2=CommandeClient 3=BonLivraison 4=Facture 5=AvoirClient
                Statut int NOT NULL DEFAULT 1,
                -- Statut: 1=Brouillon 2=ValidÃ© 3=EnvoyÃ© 4=AcceptÃ© 5=Partiellement livrÃ©
                --         6=LivrÃ© 7=FacturÃ© 8=Partiellement rÃ©glÃ© 9=RÃ©glÃ© 10=AnnulÃ© 11=ArchivÃ©
                ClientId uniqueidentifier NOT NULL,
                RepresentantId uniqueidentifier NULL,
                DocumentParentId uniqueidentifier NULL, -- Document d'origine (transformation)
                DateDocument datetime2 NOT NULL DEFAULT GETUTCDATE(),
                DateEcheance datetime2 NULL,
                DateLivraisonPrevue datetime2 NULL,
                AdresseLivraison nvarchar(500) NULL,
                DepotId uniqueidentifier NULL,
                MontantHT decimal(18,2) NOT NULL DEFAULT 0,
                MontantRemise decimal(18,2) NOT NULL DEFAULT 0,
                MontantTVA decimal(18,2) NOT NULL DEFAULT 0,
                MontantTTC decimal(18,2) NOT NULL DEFAULT 0,
                FraisLivraison decimal(18,2) NOT NULL DEFAULT 0,
                MontantAcompte decimal(18,2) NOT NULL DEFAULT 0,
                MontantRegle decimal(18,2) NOT NULL DEFAULT 0,
                TauxTVA decimal(5,2) NOT NULL DEFAULT 20,
                ConditionsPaiement nvarchar(200) NULL,
                NotesInternes nvarchar(1000) NULL,
                NotesExterne nvarchar(1000) NULL,
                EstVerrouille bit NOT NULL DEFAULT 0,
                CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
                UpdatedAt datetime2 NULL, CreatedBy nvarchar(450) NOT NULL DEFAULT '')",

            @"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='LignesDocumentVente' AND xtype='U')
              CREATE TABLE LignesDocumentVente (Id uniqueidentifier NOT NULL PRIMARY KEY,
                DocumentId uniqueidentifier NOT NULL,
                ArticleId uniqueidentifier NOT NULL,
                Designation nvarchar(200) NOT NULL,
                Quantite decimal(18,4) NOT NULL DEFAULT 1,
                QuantiteLivree decimal(18,4) NOT NULL DEFAULT 0,
                PrixUnitaireHT decimal(18,4) NOT NULL DEFAULT 0,
                TauxRemise decimal(5,2) NOT NULL DEFAULT 0,
                MontantRemise decimal(18,2) NOT NULL DEFAULT 0,
                PrixNetHT decimal(18,4) NOT NULL DEFAULT 0,
                TauxTVA decimal(5,2) NOT NULL DEFAULT 20,
                MontantTVA decimal(18,2) NOT NULL DEFAULT 0,
                MontantTTC decimal(18,2) NOT NULL DEFAULT 0,
                NumeroLot nvarchar(50) NULL,
                Ordre int NOT NULL DEFAULT 0,
                Notes nvarchar(500) NULL)",

            @"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='DocumentsAchatComm' AND xtype='U')
              CREATE TABLE DocumentsAchatComm (Id uniqueidentifier NOT NULL PRIMARY KEY,
                Numero nvarchar(30) NOT NULL, TypeDocument int NOT NULL,
                -- TypeDocument: 1=DemandeAchat 2=CommandeFournisseur 3=BonReception 4=FactureFournisseur 5=AvoirFournisseur
                Statut int NOT NULL DEFAULT 1,
                FournisseurId uniqueidentifier NOT NULL,
                DocumentParentId uniqueidentifier NULL,
                DateDocument datetime2 NOT NULL DEFAULT GETUTCDATE(),
                DateLivraisonPrevue datetime2 NULL,
                DateReceptionReelle datetime2 NULL,
                DepotId uniqueidentifier NULL,
                MontantHT decimal(18,2) NOT NULL DEFAULT 0,
                MontantRemise decimal(18,2) NOT NULL DEFAULT 0,
                MontantTVA decimal(18,2) NOT NULL DEFAULT 0,
                MontantTTC decimal(18,2) NOT NULL DEFAULT 0,
                FraisLivraison decimal(18,2) NOT NULL DEFAULT 0,
                MontantRegle decimal(18,2) NOT NULL DEFAULT 0,
                NotesInternes nvarchar(1000) NULL,
                EstVerrouille bit NOT NULL DEFAULT 0,
                CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
                UpdatedAt datetime2 NULL, CreatedBy nvarchar(450) NOT NULL DEFAULT '')",

            @"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='LignesDocumentAchat' AND xtype='U')
              CREATE TABLE LignesDocumentAchat (Id uniqueidentifier NOT NULL PRIMARY KEY,
                DocumentId uniqueidentifier NOT NULL,
                ArticleId uniqueidentifier NOT NULL,
                Designation nvarchar(200) NOT NULL,
                Quantite decimal(18,4) NOT NULL DEFAULT 1,
                QuantiteRecue decimal(18,4) NOT NULL DEFAULT 0,
                PrixUnitaireHT decimal(18,4) NOT NULL DEFAULT 0,
                TauxRemise decimal(5,2) NOT NULL DEFAULT 0,
                MontantRemise decimal(18,2) NOT NULL DEFAULT 0,
                PrixNetHT decimal(18,4) NOT NULL DEFAULT 0,
                TauxTVA decimal(5,2) NOT NULL DEFAULT 20,
                MontantTTC decimal(18,2) NOT NULL DEFAULT 0,
                NumeroLot nvarchar(50) NULL,
                Ordre int NOT NULL DEFAULT 0)",

            @"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Reglements' AND xtype='U')
              CREATE TABLE Reglements (Id uniqueidentifier NOT NULL PRIMARY KEY,
                DocumentId uniqueidentifier NOT NULL,
                TypeDocument int NOT NULL, -- 1=Vente 2=Achat
                ModeReglement int NOT NULL DEFAULT 1,
                -- 1=EspÃ¨ces 2=ChÃ¨que 3=Virement 4=CB 5=PrÃ©lÃ¨vement 6=Traite 7=Autre
                Montant decimal(18,2) NOT NULL,
                DateReglement datetime2 NOT NULL DEFAULT GETUTCDATE(),
                Reference nvarchar(100) NULL,
                Notes nvarchar(500) NULL,
                CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
                CreatedBy nvarchar(450) NOT NULL DEFAULT '')",

            @"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Acomptes' AND xtype='U')
              CREATE TABLE Acomptes (Id uniqueidentifier NOT NULL PRIMARY KEY,
                ClientId uniqueidentifier NULL, FournisseurId uniqueidentifier NULL,
                DocumentId uniqueidentifier NULL,
                Montant decimal(18,2) NOT NULL,
                MontantUtilise decimal(18,2) NOT NULL DEFAULT 0,
                DateAcompte datetime2 NOT NULL DEFAULT GETUTCDATE(),
                ModeReglement int NOT NULL DEFAULT 1,
                Reference nvarchar(100) NULL,
                Notes nvarchar(500) NULL,
                EstUtilise bit NOT NULL DEFAULT 0,
                CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
                CreatedBy nvarchar(450) NOT NULL DEFAULT '')",

            @"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='NumeroAutoComm' AND xtype='U')
              CREATE TABLE NumeroAutoComm (TypeDocument nvarchar(20) NOT NULL PRIMARY KEY,
                Prefixe nvarchar(10) NOT NULL,
                Annee int NOT NULL,
                Compteur int NOT NULL DEFAULT 0)"
        };

        foreach (var sql in tables)
        {
            await using var cmd = new SqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // CLIENTS
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    [HttpGet("clients")]
    public async Task<IActionResult> GetClients([FromQuery] string? q = null, [FromQuery] bool actifSeulement = true)
    {
        var clients = await _commercialClientService.GetClientsAsync(q, actifSeulement, HttpContext.RequestAborted);
        return Ok(clients);
    }

    [HttpPost("clients")]
    public async Task<IActionResult> CreerClient([FromBody] ClientRequest dto)
    {
        var (result, id, code) = await _commercialClientService.CreerClientAsync(
            MapClientRequest(dto),
            UserId,
            HttpContext.RequestAborted);

        return result.Succes
            ? Ok(new { succes = true, message = result.Message, id, code })
            : BadRequest(new { succes = false, message = result.Message });
    }

    [HttpPut("clients/{id:guid}")]
    public async Task<IActionResult> ModifierClient(Guid id, [FromBody] ClientRequest dto)
    {
        var result = await _commercialClientService.ModifierClientAsync(
            id,
            MapClientRequest(dto),
            HttpContext.RequestAborted);

        if (result.Succes)
            return Ok(new { succes = true, message = result.Message });

        return result.Message == "Client introuvable."
            ? NotFound(new { succes = false, message = result.Message })
            : BadRequest(new { succes = false, message = result.Message });
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    private static CommercialClientRequestDto MapClientRequest(ClientRequest dto)
        => new(
            dto.RaisonSociale,
            dto.TypeClient,
            dto.Email,
            dto.Telephone,
            dto.Adresse,
            dto.CodePostal,
            dto.Ville,
            dto.Pays,
            dto.NumeroTVA,
            dto.Siret,
            dto.DelaiPaiementJours,
            dto.TauxRemise,
            dto.PlafondCredit,
            dto.Notes,
            dto.EstActif);

    private static CommercialVenteRequestDto MapVenteRequest(DocumentVenteRequest dto)
        => new(
            dto.TypeDocument,
            dto.ClientId,
            dto.RepresentantId,
            dto.DocumentParentId,
            dto.DateDocument,
            dto.DateEcheance,
            dto.DateLivraisonPrevue,
            dto.AdresseLivraison,
            dto.DepotId,
            dto.FraisLivraison,
            dto.MontantAcompte,
            dto.TauxTVA,
            dto.ConditionsPaiement,
            dto.NotesInternes,
            dto.NotesExterne,
            dto.Lignes
                .Select(l => new CommercialVenteLigneRequestDto(
                    l.ArticleId,
                    l.Designation,
                    l.Quantite,
                    l.PrixUnitaireHT,
                    l.TauxRemise,
                    l.TauxTVA,
                    l.NumeroLot,
                    l.NumeroSerie))
                .ToList());

    private static CommercialReglementRequestDto MapReglementRequest(ReglementRequest dto)
        => new(
            dto.Montant,
            dto.ModeReglement,
            dto.DateReglement,
            dto.Reference,
            dto.Notes);

    private static CommercialAchatRequestDto MapAchatRequest(DocumentAchatRequest dto)
        => new(
            dto.TypeDocument,
            dto.FournisseurId,
            dto.DocumentParentId,
            dto.DateDocument,
            dto.DateLivraisonPrevue,
            dto.DepotId,
            dto.FraisLivraison,
            dto.NotesInternes,
            dto.Lignes
                .Select(l => new CommercialAchatLigneRequestDto(
                    l.ArticleId,
                    l.Designation,
                    l.Quantite,
                    l.PrixUnitaireHT,
                    l.TauxRemise,
                    l.TauxTVA,
                    l.NumeroLot,
                    l.NumeroSerie))
                .ToList());

    // DOCUMENTS DE VENTE
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    [HttpGet("ventes")]
    public async Task<IActionResult> GetVentes([FromQuery] int? type=null, [FromQuery] int? statut=null,
        [FromQuery] Guid? clientId=null, [FromQuery] string? q=null)
    {
        var ventes = await _commercialVenteQueryService.GetVentesAsync(type, statut, clientId, q, HttpContext.RequestAborted);
        return Ok(ventes);
    }

    [HttpGet("ventes/{id:guid}")]
    public async Task<IActionResult> GetVente(Guid id)
    {
        var vente = await _commercialVenteQueryService.GetVenteAsync(id, HttpContext.RequestAborted);
        return vente is null ? NotFound() : Ok(vente);
    }

    [HttpPut("ventes/{id:guid}")]
    public async Task<IActionResult> ModifierVente(Guid id, [FromBody] DocumentVenteRequest dto)
    {
        var result = await _commercialVenteCommandService.ModifierVenteAsync(
            id,
            MapVenteRequest(dto),
            HttpContext.RequestAborted);

        if (result.Succes)
            return Ok(new { succes = true, message = result.Message });

        return result.Message == "Document introuvable."
            ? NotFound(new { succes = false, message = result.Message })
            : BadRequest(new { succes = false, message = result.Message });
    }

    [HttpPost("ventes")]
    public async Task<IActionResult> CreerVente([FromBody] DocumentVenteRequest dto)
    {
        var (result, id, numero) = await _commercialVenteCommandService.CreerVenteAsync(
            MapVenteRequest(dto),
            UserId,
            HttpContext.RequestAborted);

        return result.Succes
            ? Ok(new { succes = true, message = result.Message, id, numero })
            : BadRequest(new { succes = false, message = result.Message });
    }
    [HttpPost("ventes/{id:guid}/transformer/{typeDoc:int}")]
    public async Task<IActionResult> TransformerVente(Guid id, int typeDoc)
    {
        var (result, newId, numero) = await _commercialVenteCommandService.TransformerVenteAsync(
            id,
            typeDoc,
            UserId,
            HttpContext.RequestAborted);

        if (result.Succes)
            return Ok(new { succes = true, message = result.Message, id = newId, numero });

        return result.Message == "Document introuvable."
            ? NotFound(new { succes = false, message = result.Message })
            : BadRequest(new { succes = false, message = result.Message });
    }

    [HttpPost("ventes/{id:guid}/valider")]
    public async Task<IActionResult> ValiderVente(Guid id)
    {
        var result = await _commercialVenteCommandService.SetStatutVenteAsync(id, 2, HttpContext.RequestAborted);
        return result.Succes
            ? Ok(new { succes = true, message = result.Message })
            : BadRequest(new { succes = false, message = result.Message });
    }

    [HttpPost("ventes/{id:guid}/comptabiliser")]
    public async Task<IActionResult> ComptabiliserVente(Guid id)
    {
        var result = await _commercialVenteCommandService.SetStatutVenteAsync(id, 5, HttpContext.RequestAborted);
        return result.Succes
            ? Ok(new { succes = true, message = result.Message })
            : BadRequest(new { succes = false, message = result.Message });
    }

    [HttpPost("ventes/{id:guid}/annuler")]
    public async Task<IActionResult> AnnulerVente(Guid id)
    {
        var result = await _commercialVenteCommandService.AnnulerVenteAsync(id, HttpContext.RequestAborted);
        return result.Succes
            ? Ok(new { succes = true, message = result.Message })
            : result.Message == "Document introuvable."
                ? NotFound(new { succes = false, message = result.Message })
                : BadRequest(new { succes = false, message = result.Message });
    }

    [HttpPost("ventes/{id:guid}/reglement")]
    public async Task<IActionResult> AjouterReglement(Guid id, [FromBody] ReglementRequest dto)
    {
        var (result, solde, estRegle) = await _commercialVenteCommandService.AjouterReglementAsync(
            id,
            MapReglementRequest(dto),
            UserId,
            HttpContext.RequestAborted);

        if (result.Succes)
            return Ok(new { succes = true, message = result.Message, solde, estRegle });

        return result.Message == "Document introuvable."
            ? NotFound(new { succes = false, message = result.Message })
            : BadRequest(new { succes = false, message = result.Message });
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // DOCUMENTS D'ACHAT
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    [HttpGet("achats")]
    public async Task<IActionResult> GetAchats([FromQuery] int? type=null, [FromQuery] Guid? fournisseurId=null)
    {
        var achats = await _commercialAchatQueryService.GetAchatsAsync(type, fournisseurId, HttpContext.RequestAborted);
        return Ok(achats);
    }

    [HttpGet("achats/{id:guid}")]
    public async Task<IActionResult> GetAchat(Guid id)
    {
        var achat = await _commercialAchatQueryService.GetAchatAsync(id, HttpContext.RequestAborted);
        return achat is null ? NotFound() : Ok(achat);
    }

    [HttpPost("achats")]
    public async Task<IActionResult> CreerAchat([FromBody] DocumentAchatRequest dto)
    {
        var (result, id, numero) = await _commercialAchatCommandService.CreerAchatAsync(
            MapAchatRequest(dto),
            UserId,
            HttpContext.RequestAborted);

        return result.Succes
            ? Ok(new { succes = true, message = result.Message, id, numero })
            : BadRequest(new { succes = false, message = result.Message });
    }

    [HttpPost("achats/{id:guid}/transformer/{typeDoc:int}")]
    public async Task<IActionResult> TransformerAchat(Guid id, int typeDoc)
    {
        var (result, newId, numero) = await _commercialAchatCommandService.TransformerAchatAsync(
            id,
            typeDoc,
            UserId,
            HttpContext.RequestAborted);

        if (result.Succes)
            return Ok(new { succes = true, message = result.Message, id = newId, numero });

        return result.Message == "Document introuvable."
            ? NotFound(new { succes = false, message = result.Message })
            : BadRequest(new { succes = false, message = result.Message });
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // TABLEAU DE BORD COMMERCIAL
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard()
    {
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        await EnsureTablesAsync(conn);
        var stats = new Dictionary<string,object>();
        try
        {
            await using var cmd = new SqlCommand(@"
                SELECT
                    (SELECT COUNT(1) FROM Clients WHERE EstActif=1) AS NbClients,
                    (SELECT COUNT(1) FROM DocumentsVente WHERE TypeDocument=1 AND Statut NOT IN (9,10,11)) AS DevisOuverts,
                    (SELECT COUNT(1) FROM DocumentsVente WHERE TypeDocument=2 AND Statut NOT IN (9,10,11)) AS CommandesEnCours,
                    (SELECT COUNT(1) FROM DocumentsVente WHERE TypeDocument=4 AND Statut IN (7,8)) AS FacturesNonReglees,
                    (SELECT ISNULL(SUM(MontantTTC),0) FROM DocumentsVente WHERE TypeDocument=4 AND MONTH(DateDocument)=MONTH(GETDATE()) AND YEAR(DateDocument)=YEAR(GETDATE())) AS CaMois,
                    (SELECT ISNULL(SUM(MontantTTC-MontantRegle),0) FROM DocumentsVente WHERE TypeDocument=4 AND Statut IN (7,8)) AS EncoursClient,
                    (SELECT COUNT(1) FROM DocumentsAchatComm WHERE TypeDocument=2 AND Statut NOT IN (9,10)) AS CommandesFourn
            ", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
                stats = new Dictionary<string,object> {
                    {"nbClients",r.GetInt32(0)}, {"devisOuverts",r.GetInt32(1)},
                    {"commandesEnCours",r.GetInt32(2)}, {"facturesNonReglees",r.GetInt32(3)},
                    {"caMois",r.GetDecimal(4)}, {"encoursClient",r.GetDecimal(5)},
                    {"commandesFourn",r.GetInt32(6)}
                };
        }
        catch { }
        return Ok(stats);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // RÃˆGLEMENTS â€” LISTE ET DÃ‰TAIL
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [HttpGet("reglements")]
    public async Task<IActionResult> GetReglements([FromQuery] Guid? documentId=null,
        [FromQuery] int? typeDocument=null, [FromQuery] int? modeReglement=null,
        [FromQuery] DateTime? du=null, [FromQuery] DateTime? au=null)
    {
        var list = new List<object>();
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        await EnsureTablesAsync(conn);

        var where = new List<string>{ "1=1" };
        if (documentId.HasValue) where.Add("r.DocumentId=@docId");
        if (typeDocument.HasValue) where.Add("r.TypeDocument=@typeDoc");
        if (modeReglement.HasValue) where.Add("r.ModeReglement=@mode");
        if (du.HasValue) where.Add("r.DateReglement >= @du");
        if (au.HasValue) where.Add("r.DateReglement <= @au");

        var sql = $@"
            SELECT r.Id, r.DocumentId, r.TypeDocument, r.ModeReglement, r.Montant,
                   r.DateReglement, r.Reference, r.Notes, r.CreatedAt,
                   ISNULL(dv.Numero, da.Numero) AS NumeroDoc,
                   ISNULL(c.RaisonSociale, f.RaisonSociale) AS TiersNom
            FROM Reglements r
            LEFT JOIN DocumentsVente dv ON dv.Id = r.DocumentId AND r.TypeDocument=1
            LEFT JOIN DocumentsAchatComm da ON da.Id = r.DocumentId AND r.TypeDocument=2
            LEFT JOIN Clients c ON c.Id = dv.ClientId
            LEFT JOIN Fournisseurs f ON f.Id = da.FournisseurId
            WHERE {string.Join(" AND ", where)}
            ORDER BY r.DateReglement DESC";

        await using var cmd = new SqlCommand(sql, conn);
        if (documentId.HasValue) cmd.Parameters.AddWithValue("@docId", documentId.Value);
        if (typeDocument.HasValue) cmd.Parameters.AddWithValue("@typeDoc", typeDocument.Value);
        if (modeReglement.HasValue) cmd.Parameters.AddWithValue("@mode", modeReglement.Value);
        if (du.HasValue) cmd.Parameters.AddWithValue("@du", du.Value);
        if (au.HasValue) cmd.Parameters.AddWithValue("@au", au.Value.AddDays(1));
        await using var r2 = await cmd.ExecuteReaderAsync();
        while (await r2.ReadAsync())
            list.Add(new {
                Id=r2.GetGuid(0), DocumentId=r2.GetGuid(1),
                TypeDocument=r2.GetInt32(2), ModeReglement=r2.GetInt32(3),
                ModeLibelle=GetModeLibelle(r2.GetInt32(3)),
                Montant=r2.GetDecimal(4), DateReglement=r2.GetDateTime(5),
                Reference=r2.IsDBNull(6)?null:r2.GetString(6),
                Notes=r2.IsDBNull(7)?null:r2.GetString(7),
                CreatedAt=r2.GetDateTime(8),
                NumeroDoc=r2.IsDBNull(9)?null:r2.GetString(9),
                TiersNom=r2.IsDBNull(10)?null:r2.GetString(10)
            });
        return Ok(list);
    }

    [HttpDelete("reglements/{id:guid}")]
    public async Task<IActionResult> SupprimerReglement(Guid id)
    {
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        // RÃ©cupÃ©rer le reglement avant suppression pour recalculer le solde
        Guid docId; decimal montant; int typeDoc;
        await using (var sel = new SqlCommand(
            "SELECT DocumentId, Montant, TypeDocument FROM Reglements WHERE Id=@id", conn))
        {
            sel.Parameters.AddWithValue("@id", id);
            await using var r = await sel.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return NotFound(new{succes=false,message="RÃ¨glement introuvable."});
            docId=r.GetGuid(0); montant=r.GetDecimal(1); typeDoc=r.GetInt32(2);
        }
        await using (var del = new SqlCommand("DELETE FROM Reglements WHERE Id=@id", conn))
        { del.Parameters.AddWithValue("@id",id); await del.ExecuteNonQueryAsync(); }

        // Recalculer le solde du document
        var table = typeDoc==1 ? "DocumentsVente" : "DocumentsAchatComm";
        await using var upd = new SqlCommand($@"
            UPDATE {table} SET
                MontantRegle = ISNULL((SELECT SUM(Montant) FROM Reglements WHERE DocumentId=@doc),0),
                EstVerrouille = 0,
                Statut = CASE
                    WHEN ISNULL((SELECT SUM(Montant) FROM Reglements WHERE DocumentId=@doc),0) = 0 THEN 2
                    ELSE 3 END,
                UpdatedAt = GETUTCDATE()
            WHERE Id=@doc", conn);
        upd.Parameters.AddWithValue("@doc", docId);
        await upd.ExecuteNonQueryAsync();
        return Ok(new{succes=true,message="RÃ¨glement supprimÃ© et solde recalculÃ©."});
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // ACOMPTES
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [HttpGet("acomptes")]
    public async Task<IActionResult> GetAcomptes([FromQuery] Guid? clientId=null,
        [FromQuery] Guid? fournisseurId=null, [FromQuery] bool nonUtilisesSeulement=false)
    {
        var list = new List<object>();
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        await EnsureTablesAsync(conn);

        var where = new List<string>{"1=1"};
        if (clientId.HasValue) where.Add("a.ClientId=@cid");
        if (fournisseurId.HasValue) where.Add("a.FournisseurId=@fid");
        if (nonUtilisesSeulement) where.Add("a.EstUtilise=0 AND a.MontantUtilise < a.Montant");

        await using var cmd = new SqlCommand($@"
            SELECT a.Id, a.ClientId, a.FournisseurId, a.DocumentId,
                   a.Montant, a.MontantUtilise, a.Montant-a.MontantUtilise AS MontantDisponible,
                   a.DateAcompte, a.ModeReglement, a.Reference, a.Notes, a.EstUtilise, a.CreatedAt,
                   c.RaisonSociale AS ClientNom, f.RaisonSociale AS FournisseurNom,
                   d.Numero AS NumeroDoc
            FROM Acomptes a
            LEFT JOIN Clients c ON c.Id=a.ClientId
            LEFT JOIN Fournisseurs f ON f.Id=a.FournisseurId
            LEFT JOIN DocumentsVente d ON d.Id=a.DocumentId
            WHERE {string.Join(" AND ", where)}
            ORDER BY a.DateAcompte DESC", conn);
        if (clientId.HasValue) cmd.Parameters.AddWithValue("@cid", clientId.Value);
        if (fournisseurId.HasValue) cmd.Parameters.AddWithValue("@fid", fournisseurId.Value);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new {
                Id=r.GetGuid(0), ClientId=r.IsDBNull(1)?null:(Guid?)r.GetGuid(1),
                FournisseurId=r.IsDBNull(2)?null:(Guid?)r.GetGuid(2),
                DocumentId=r.IsDBNull(3)?null:(Guid?)r.GetGuid(3),
                Montant=r.GetDecimal(4), MontantUtilise=r.GetDecimal(5), MontantDisponible=r.GetDecimal(6),
                DateAcompte=r.GetDateTime(7), ModeReglement=r.GetInt32(8),
                ModeLibelle=GetModeLibelle(r.GetInt32(8)),
                Reference=r.IsDBNull(9)?null:r.GetString(9),
                Notes=r.IsDBNull(10)?null:r.GetString(10),
                EstUtilise=r.GetBoolean(11), CreatedAt=r.GetDateTime(12),
                ClientNom=r.IsDBNull(13)?null:r.GetString(13),
                FournisseurNom=r.IsDBNull(14)?null:r.GetString(14),
                NumeroDoc=r.IsDBNull(15)?null:r.GetString(15)
            });
        return Ok(list);
    }

    [HttpPost("acomptes")]
    public async Task<IActionResult> CreerAcompte([FromBody] AcompteRequest dto)
    {
        if (dto.ClientId == null && dto.FournisseurId == null)
            return BadRequest(new{succes=false,message="Client ou fournisseur obligatoire."});
        if (dto.Montant <= 0)
            return BadRequest(new{succes=false,message="Montant invalide."});

        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        await EnsureTablesAsync(conn);
        var id = Guid.NewGuid();
        await using var cmd = new SqlCommand(@"
            INSERT INTO Acomptes (Id,ClientId,FournisseurId,DocumentId,Montant,MontantUtilise,
                DateAcompte,ModeReglement,Reference,Notes,EstUtilise,CreatedAt,CreatedBy)
            VALUES (@id,@cid,@fid,@docId,@mnt,0,@date,@mode,@ref,@notes,0,GETUTCDATE(),@user)", conn);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@cid", (object?)dto.ClientId??DBNull.Value);
        cmd.Parameters.AddWithValue("@fid", (object?)dto.FournisseurId??DBNull.Value);
        cmd.Parameters.AddWithValue("@docId", (object?)dto.DocumentId??DBNull.Value);
        cmd.Parameters.AddWithValue("@mnt", dto.Montant);
        cmd.Parameters.AddWithValue("@date", dto.DateAcompte??DateTime.Today);
        cmd.Parameters.AddWithValue("@mode", dto.ModeReglement);
        cmd.Parameters.AddWithValue("@ref", (object?)dto.Reference??DBNull.Value);
        cmd.Parameters.AddWithValue("@notes", (object?)dto.Notes??DBNull.Value);
        cmd.Parameters.AddWithValue("@user", UserId);
        await cmd.ExecuteNonQueryAsync();
        return Ok(new{succes=true,message="Acompte enregistrÃ©.",id});
    }

    [HttpPost("acomptes/{acompteId:guid}/imputer/{documentId:guid}")]
    public async Task<IActionResult> ImputerAcompte(Guid acompteId, Guid documentId,
        [FromBody] ImputationRequest dto)
    {
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        // Lire l'acompte
        decimal montantDispo;
        await using (var sel = new SqlCommand(
            "SELECT Montant-MontantUtilise FROM Acomptes WHERE Id=@id AND EstUtilise=0", conn))
        {
            sel.Parameters.AddWithValue("@id", acompteId);
            var val = await sel.ExecuteScalarAsync();
            if (val == null || val == DBNull.Value)
                return BadRequest(new{succes=false,message="Acompte introuvable ou dÃ©jÃ  utilisÃ©."});
            montantDispo = Convert.ToDecimal(val);
        }
        var montant = dto.Montant > 0 ? Math.Min(dto.Montant, montantDispo) : montantDispo;

        // Lire le solde du document
        decimal ttc, regle;
        await using (var sel = new SqlCommand(
            "SELECT MontantTTC,MontantRegle,EstVerrouille FROM DocumentsVente WHERE Id=@id", conn))
        {
            sel.Parameters.AddWithValue("@id", documentId);
            await using var r = await sel.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return NotFound(new{succes=false,message="Document introuvable."});
            if (r.GetBoolean(2)) return BadRequest(new{succes=false,message="Document dÃ©jÃ  rÃ©glÃ©."});
            ttc=r.GetDecimal(0); regle=r.GetDecimal(1);
        }
        var solde = ttc - regle;
        if (montant > solde) montant = solde;
        if (montant <= 0) return BadRequest(new{succes=false,message="Solde dÃ©jÃ  rÃ©glÃ©."});

        // CrÃ©er un rÃ¨glement liÃ© Ã  l'acompte
        await using (var rCmd = new SqlCommand(@"
            INSERT INTO Reglements (Id,DocumentId,TypeDocument,ModeReglement,Montant,
                DateReglement,Reference,Notes,CreatedAt,CreatedBy)
            VALUES (NEWID(),@doc,1,9,@mnt,GETUTCDATE(),@ref,N'Imputation acompte',GETUTCDATE(),@user)", conn))
        {
            rCmd.Parameters.AddWithValue("@doc",documentId);
            rCmd.Parameters.AddWithValue("@mnt",montant);
            rCmd.Parameters.AddWithValue("@ref",$"ACOMPTE-{acompteId.ToString()[..8].ToUpperInvariant()}");
            rCmd.Parameters.AddWithValue("@user",UserId);
            await rCmd.ExecuteNonQueryAsync();
        }
        // Mettre Ã  jour l'acompte
        var newMontantUtilise = 0m;
        await using (var sel = new SqlCommand("SELECT MontantUtilise+@m FROM Acomptes WHERE Id=@id",conn))
        {
            sel.Parameters.AddWithValue("@m",montant); sel.Parameters.AddWithValue("@id",acompteId);
            newMontantUtilise = Convert.ToDecimal(await sel.ExecuteScalarAsync()??0);
        }
        await using (var upd = new SqlCommand(@"
            UPDATE Acomptes SET MontantUtilise=@mu,
                EstUtilise=CASE WHEN @mu>=Montant THEN 1 ELSE 0 END,
                UpdatedAt=GETUTCDATE()
            WHERE Id=@id", conn))
        {
            upd.Parameters.AddWithValue("@mu",newMontantUtilise);
            upd.Parameters.AddWithValue("@id",acompteId);
            await upd.ExecuteNonQueryAsync();
        }
        // Mettre Ã  jour le document
        var newRegle = regle+montant;
        var totRegle = newRegle >= ttc;
        await using var updDoc = new SqlCommand(@"
            UPDATE DocumentsVente SET MontantRegle=@r, EstVerrouille=@v,
                Statut=CASE WHEN @v=1 THEN 9 ELSE 8 END,
                MontantAcompte=MontantAcompte+@m, UpdatedAt=GETUTCDATE()
            WHERE Id=@docId", conn);
        updDoc.Parameters.AddWithValue("@r",newRegle);
        updDoc.Parameters.AddWithValue("@v",totRegle);
        updDoc.Parameters.AddWithValue("@m",montant);
        updDoc.Parameters.AddWithValue("@docId",documentId);
        await updDoc.ExecuteNonQueryAsync();
        return Ok(new{succes=true,
            message=$"Acompte de {montant:N2} â‚¬ imputÃ© sur le document.",
            montantImpute=montant, solde=ttc-newRegle, estRegle=totRegle});
    }

    private static string GetModeLibelle(int m) => m switch {
        1=>"EspÃ¨ces", 2=>"ChÃ¨que", 3=>"Virement", 4=>"Carte bancaire",
        5=>"PrÃ©lÃ¨vement", 6=>"Traite", 7=>"Autre", 9=>"Acompte imputÃ©", _=>"?"
    };

}

// â”€â”€â”€ DTOs â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
public class ClientRequest {
    public string RaisonSociale { get; set; } = "";
    public int TypeClient { get; set; } = 2;
    public string? Email { get; set; }
    public string? Telephone { get; set; }
    public string? Adresse { get; set; }
    public string? CodePostal { get; set; }
    public string? Ville { get; set; }
    public string? Pays { get; set; } = "France";
    public string? NumeroTVA { get; set; }
    public string? Siret { get; set; }
    public int DelaiPaiementJours { get; set; } = 30;
    public decimal TauxRemise { get; set; } = 0;
    public decimal PlafondCredit { get; set; } = 0;
    public string? Notes { get; set; }
    public bool EstActif { get; set; } = true;
}

public class LigneDocumentRequest {
    public Guid ArticleId { get; set; }
    public string Designation { get; set; } = "";
    public decimal Quantite { get; set; } = 1;
    public decimal PrixUnitaireHT { get; set; }
    public decimal TauxRemise { get; set; } = 0;
    public decimal TauxTVA { get; set; } = 20;
    public string? NumeroLot { get; set; }
    public string? NumeroSerie { get; set; }
}

public class DocumentVenteRequest {
    public int TypeDocument { get; set; } = 1;
    public Guid ClientId { get; set; }
    public Guid? RepresentantId { get; set; }
    public Guid? DocumentParentId { get; set; }
    public DateTime? DateDocument { get; set; }
    public DateTime? DateEcheance { get; set; }
    public DateTime? DateLivraisonPrevue { get; set; }
    public string? AdresseLivraison { get; set; }
    public Guid? DepotId { get; set; }
    public decimal FraisLivraison { get; set; } = 0;
    public decimal MontantAcompte { get; set; } = 0;
    public decimal TauxTVA { get; set; } = 20;
    public string? ConditionsPaiement { get; set; }
    public string? NotesInternes { get; set; }
    public string? NotesExterne { get; set; }
    public List<LigneDocumentRequest> Lignes { get; set; } = new();
}

public class DocumentAchatRequest {
    public int TypeDocument { get; set; } = 1;
    public Guid FournisseurId { get; set; }
    public Guid? DocumentParentId { get; set; }
    public DateTime? DateDocument { get; set; }
    public DateTime? DateLivraisonPrevue { get; set; }
    public Guid? DepotId { get; set; }
    public decimal FraisLivraison { get; set; } = 0;
    public string? NotesInternes { get; set; }
    public List<LigneDocumentRequest> Lignes { get; set; } = new();
}

public class ReglementRequest {
    public decimal Montant { get; set; }
    public int ModeReglement { get; set; } = 1;
    public DateTime? DateReglement { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
}

public class AcompteRequest {
    public Guid? ClientId { get; set; }
    public Guid? FournisseurId { get; set; }
    public Guid? DocumentId { get; set; }
    public decimal Montant { get; set; }
    public int ModeReglement { get; set; } = 1;
    public DateTime? DateAcompte { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
}

public class ImputationRequest {
    public decimal Montant { get; set; } // 0 = utiliser tout le disponible
}




