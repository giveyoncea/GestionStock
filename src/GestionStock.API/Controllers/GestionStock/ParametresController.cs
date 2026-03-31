using GestionStock.Application.DTOs;
using GestionStock.API.Services;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace GestionStock.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[Tags("Paramètres")]
public class ParametresController : ControllerBase
{
    private readonly ITenantService _tenant;
    private readonly IConfiguration _config;
    private string UserId => User.FindFirstValue("sub") ?? "system";
    private string ConnStr { get {
        var t = User.FindFirstValue("tenant");
        return !string.IsNullOrEmpty(t) ? _tenant.GetConnectionString(t) : _config.GetConnectionString("DefaultConnection")!;
    } }

    public ParametresController(ITenantService tenant, IConfiguration config)
    {
        _tenant = tenant;
        _config = config;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Get()
    {
        try
        {
            var dto = new ParametresDto();
            var sql = "SELECT * FROM Parametres WHERE Id = 1";

            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                dto.RaisonSociale = reader["RaisonSociale"]?.ToString() ?? "";
                dto.Siret = reader["Siret"]?.ToString() ?? "";
                dto.NumTVA = reader["NumTVA"]?.ToString() ?? "";
                dto.Telephone = reader["Telephone"]?.ToString() ?? "";
                dto.Email = reader["Email"]?.ToString() ?? "";
                dto.SiteWeb = reader["SiteWeb"]?.ToString() ?? "";
                dto.FormeJuridique = reader["FormeJuridique"]?.ToString() ?? "";
                dto.Adresse = reader["Adresse"]?.ToString() ?? "";
                dto.CodePostal = reader["CodePostal"]?.ToString() ?? "";
                dto.Ville = reader["Ville"]?.ToString() ?? "";
                dto.Region = reader["Region"]?.ToString() ?? "";
                dto.Pays = reader["Pays"]?.ToString() ?? "France";
                dto.MethodeValorisation = reader["MethodeValorisation"]?.ToString() ?? "FEFO";
                dto.GabaritInterface = reader["GabaritInterface"]?.ToString() ?? "STANDARD";
                dto.LogoEntreprise = reader["LogoEntreprise"]?.ToString() ?? "";
                dto.FormatImpressionDocuments = reader["FormatImpressionDocuments"]?.ToString() ?? "STANDARD";
                dto.FormatImpressionRecus = reader["FormatImpressionRecus"]?.ToString() ?? "STANDARD";
                dto.FormatPapierDocuments = reader["FormatPapierDocuments"]?.ToString() ?? "A4";
                dto.ImprimanteDocumentsDefaut = reader["ImprimanteDocumentsDefaut"]?.ToString() ?? "";
                dto.FormatPapierRecus = reader["FormatPapierRecus"]?.ToString() ?? "A5";
                dto.ImprimanteRecusDefaut = reader["ImprimanteRecusDefaut"]?.ToString() ?? "";
                dto.Devise = reader["Devise"]?.ToString() ?? "EUR";
                dto.SymboleDevise = reader["SymboleDevise"]?.ToString() ?? dto.Devise;
                dto.NombreDecimalesMontant = reader["NombreDecimalesMontant"] is DBNull ? 2 : Convert.ToInt32(reader["NombreDecimalesMontant"]);
                dto.NombreDecimalesQuantite = reader["NombreDecimalesQuantite"] is DBNull ? 3 : Convert.ToInt32(reader["NombreDecimalesQuantite"]);
                dto.TauxTVA = reader["TauxTVA"] is DBNull ? 20m : Convert.ToDecimal(reader["TauxTVA"]);
                dto.DelaiAlerteDLUO = reader["DelaiAlerteDLUO"] is DBNull ? 30 : Convert.ToInt32(reader["DelaiAlerteDLUO"]);
                dto.AutoriserStockNegatif = reader["AutoriserStockNegatif"] is not DBNull && Convert.ToBoolean(reader["AutoriserStockNegatif"]);
                dto.AlerteMailActif = reader["AlerteMailActif"] is not DBNull && Convert.ToBoolean(reader["AlerteMailActif"]);
                dto.PrefixeCA = reader["PrefixeCA"]?.ToString() ?? "CA";
                dto.PrefixeArt = reader["PrefixeArt"]?.ToString() ?? "ART";
                dto.PrefixeLot = reader["PrefixeLot"]?.ToString() ?? "LOT";
                dto.PrefixeInv = reader["PrefixeInv"]?.ToString() ?? "INV";
                dto.Banque = reader["Banque"]?.ToString() ?? "";
                dto.Iban = reader["Iban"]?.ToString() ?? "";
                dto.Bic = reader["Bic"]?.ToString() ?? "";
                dto.DelaiPaiement = reader["DelaiPaiement"] is DBNull ? 30 : Convert.ToInt32(reader["DelaiPaiement"]);
                dto.UpdatedAt = reader["UpdatedAt"] is DBNull ? null : Convert.ToDateTime(reader["UpdatedAt"]);
                dto.UpdatedBy = reader["UpdatedBy"]?.ToString() ?? "";
            }

            return Ok(dto);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { succes = false, message = ex.Message });
        }
    }

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Save([FromBody] ParametresDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.RaisonSociale))
            return BadRequest(new { succes = false, message = "La raison sociale est obligatoire." });

        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();

            // Vérifier si la ligne existe
            var exists = false;
            await using (var chk = new SqlCommand("SELECT COUNT(1) FROM Parametres WHERE Id=1", conn))
                exists = Convert.ToInt32(await chk.ExecuteScalarAsync() ?? 0) > 0;

            if (exists)
            {
                await using var currentCmd = new SqlCommand(@"
                    SELECT Devise, SymboleDevise, NombreDecimalesMontant, NombreDecimalesQuantite
                    FROM Parametres WHERE Id=1", conn);
                await using var currentReader = await currentCmd.ExecuteReaderAsync();
                if (await currentReader.ReadAsync())
                {
                    dto.Devise = currentReader["Devise"]?.ToString() ?? dto.Devise;
                    dto.SymboleDevise = currentReader["SymboleDevise"]?.ToString() ?? dto.SymboleDevise;
                    dto.NombreDecimalesMontant = currentReader["NombreDecimalesMontant"] is DBNull
                        ? dto.NombreDecimalesMontant
                        : Convert.ToInt32(currentReader["NombreDecimalesMontant"]);
                    dto.NombreDecimalesQuantite = currentReader["NombreDecimalesQuantite"] is DBNull
                        ? dto.NombreDecimalesQuantite
                        : Convert.ToInt32(currentReader["NombreDecimalesQuantite"]);
                }
            }
            else
            {
                dto.GabaritInterface = string.IsNullOrWhiteSpace(dto.GabaritInterface) ? "STANDARD" : dto.GabaritInterface.Trim().ToUpperInvariant();
                dto.Devise = string.IsNullOrWhiteSpace(dto.Devise) ? "EUR" : dto.Devise.Trim().ToUpperInvariant();
                dto.SymboleDevise = string.IsNullOrWhiteSpace(dto.SymboleDevise) ? dto.Devise : dto.SymboleDevise.Trim();
                dto.NombreDecimalesMontant = Math.Clamp(dto.NombreDecimalesMontant, 0, 6);
                dto.NombreDecimalesQuantite = Math.Clamp(dto.NombreDecimalesQuantite, 0, 6);
            }

            dto.GabaritInterface = dto.GabaritInterface switch
            {
                "MARBRE_BLEU" => "MARBRE_BLEU",
                _ => "STANDARD"
            };
            dto.LogoEntreprise ??= string.Empty;
            dto.FormatImpressionDocuments = dto.FormatImpressionDocuments switch
            {
                "PROFESSIONNEL" => "PROFESSIONNEL",
                "COMPACT" => "COMPACT",
                _ => "STANDARD"
            };
            dto.FormatImpressionRecus = dto.FormatImpressionRecus switch
            {
                "PROFESSIONNEL" => "PROFESSIONNEL",
                "COMPACT" => "COMPACT",
                _ => "STANDARD"
            };
            dto.FormatPapierDocuments = dto.FormatPapierDocuments switch
            {
                "80MM" => "80MM",
                "A5" => "A5",
                _ => "A4"
            };
            dto.FormatPapierRecus = dto.FormatPapierRecus switch
            {
                "A4" => "A4",
                "80MM" => "80MM",
                "2X_A5_A4" => "2X_A5_A4",
                _ => "A5"
            };
            dto.ImprimanteDocumentsDefaut = (dto.ImprimanteDocumentsDefaut ?? string.Empty).Trim();
            dto.ImprimanteRecusDefaut = (dto.ImprimanteRecusDefaut ?? string.Empty).Trim();

            string sql = exists
                ? @"UPDATE Parametres SET
                    RaisonSociale=@v1, Siret=@v2, NumTVA=@v3, Telephone=@v4, Email=@v5,
                    SiteWeb=@v6, FormeJuridique=@v7, Adresse=@v8, CodePostal=@v9, Ville=@v10,
                    Region=@v11, Pays=@v12, MethodeValorisation=@v13, GabaritInterface=@v14, LogoEntreprise=@v15,
                    FormatImpressionDocuments=@v16, FormatImpressionRecus=@v17,
                    FormatPapierDocuments=@v18, ImprimanteDocumentsDefaut=@v19,
                    FormatPapierRecus=@v20, ImprimanteRecusDefaut=@v21, Devise=@v22,
                    SymboleDevise=@v23, NombreDecimalesMontant=@v24, NombreDecimalesQuantite=@v25,
                    TauxTVA=@v26, DelaiAlerteDLUO=@v27, AutoriserStockNegatif=@v28, AlerteMailActif=@v29,
                    PrefixeCA=@v30, PrefixeArt=@v31,
                    PrefixeLot=@v32, PrefixeInv=@v33, Banque=@v34, Iban=@v35, Bic=@v36,
                    DelaiPaiement=@v37, UpdatedAt=@v38, UpdatedBy=@v39
                    WHERE Id=1"
                : @"INSERT INTO Parametres (Id,
                    RaisonSociale, Siret, NumTVA, Telephone, Email, SiteWeb, FormeJuridique,
                    Adresse, CodePostal, Ville, Region, Pays, MethodeValorisation, GabaritInterface, LogoEntreprise,
                    FormatImpressionDocuments, FormatImpressionRecus,
                    FormatPapierDocuments, ImprimanteDocumentsDefaut, FormatPapierRecus, ImprimanteRecusDefaut,
                    Devise, SymboleDevise,
                    NombreDecimalesMontant, NombreDecimalesQuantite, TauxTVA,
                    DelaiAlerteDLUO, AutoriserStockNegatif, AlerteMailActif,
                    PrefixeCA, PrefixeArt, PrefixeLot, PrefixeInv,
                    Banque, Iban, Bic, DelaiPaiement, UpdatedAt, UpdatedBy)
                    VALUES (1,
                    @v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14,@v15,@v16,@v17,@v18,@v19,@v20,@v21,@v22,@v23,
                    @v24,@v25,@v26,@v27,@v28,@v29,@v30,@v31,@v32,@v33,@v34,@v35,@v36,@v37,@v38,@v39)";

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@v1",  dto.RaisonSociale);
            cmd.Parameters.AddWithValue("@v2",  dto.Siret);
            cmd.Parameters.AddWithValue("@v3",  dto.NumTVA);
            cmd.Parameters.AddWithValue("@v4",  dto.Telephone);
            cmd.Parameters.AddWithValue("@v5",  dto.Email);
            cmd.Parameters.AddWithValue("@v6",  dto.SiteWeb);
            cmd.Parameters.AddWithValue("@v7",  dto.FormeJuridique);
            cmd.Parameters.AddWithValue("@v8",  dto.Adresse);
            cmd.Parameters.AddWithValue("@v9",  dto.CodePostal);
            cmd.Parameters.AddWithValue("@v10", dto.Ville);
            cmd.Parameters.AddWithValue("@v11", dto.Region);
            cmd.Parameters.AddWithValue("@v12", dto.Pays);
            cmd.Parameters.AddWithValue("@v13", dto.MethodeValorisation);
            cmd.Parameters.AddWithValue("@v14", dto.GabaritInterface);
            cmd.Parameters.AddWithValue("@v15", dto.LogoEntreprise);
            cmd.Parameters.AddWithValue("@v16", dto.FormatImpressionDocuments);
            cmd.Parameters.AddWithValue("@v17", dto.FormatImpressionRecus);
            cmd.Parameters.AddWithValue("@v18", dto.FormatPapierDocuments);
            cmd.Parameters.AddWithValue("@v19", dto.ImprimanteDocumentsDefaut);
            cmd.Parameters.AddWithValue("@v20", dto.FormatPapierRecus);
            cmd.Parameters.AddWithValue("@v21", dto.ImprimanteRecusDefaut);
            cmd.Parameters.AddWithValue("@v22", dto.Devise);
            cmd.Parameters.AddWithValue("@v23", dto.SymboleDevise);
            cmd.Parameters.AddWithValue("@v24", dto.NombreDecimalesMontant);
            cmd.Parameters.AddWithValue("@v25", dto.NombreDecimalesQuantite);
            cmd.Parameters.AddWithValue("@v26", dto.TauxTVA);
            cmd.Parameters.AddWithValue("@v27", dto.DelaiAlerteDLUO);
            cmd.Parameters.AddWithValue("@v28", dto.AutoriserStockNegatif);
            cmd.Parameters.AddWithValue("@v29", dto.AlerteMailActif);
            cmd.Parameters.AddWithValue("@v30", dto.PrefixeCA);
            cmd.Parameters.AddWithValue("@v31", dto.PrefixeArt);
            cmd.Parameters.AddWithValue("@v32", dto.PrefixeLot);
            cmd.Parameters.AddWithValue("@v33", dto.PrefixeInv);
            cmd.Parameters.AddWithValue("@v34", dto.Banque);
            cmd.Parameters.AddWithValue("@v35", dto.Iban);
            cmd.Parameters.AddWithValue("@v36", dto.Bic);
            cmd.Parameters.AddWithValue("@v37", dto.DelaiPaiement);
            cmd.Parameters.AddWithValue("@v38", DateTime.UtcNow);
            cmd.Parameters.AddWithValue("@v39", UserId);

            await cmd.ExecuteNonQueryAsync();
            return Ok(new { succes = true, message = "Paramètres enregistrés avec succès." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { succes = false, message = $"Erreur : {ex.Message}" });
        }
    }

    // ─── PARAMÈTRES COMPTABLES ────────────────────────────────────────────────
    private async Task EnsureComptabiliteTableAsync(SqlConnection conn)
    {
        await using var cmd = new SqlCommand(@"
            IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ParametresComptables' AND xtype='U')
            CREATE TABLE ParametresComptables (
                Id int NOT NULL PRIMARY KEY DEFAULT 1,
                -- Comptes généraux clients
                CompteClientDefaut nvarchar(20) NOT NULL DEFAULT '411000',
                CompteClientEtranger nvarchar(20) NOT NULL DEFAULT '411100',
                CompteClientDouteux nvarchar(20) NOT NULL DEFAULT '416000',
                CompteAcompteClient nvarchar(20) NOT NULL DEFAULT '419000',
                -- Comptes généraux fournisseurs
                CompteFournisseurDefaut nvarchar(20) NOT NULL DEFAULT '401000',
                CompteFournisseurEtranger nvarchar(20) NOT NULL DEFAULT '401100',
                CompteAcompteFournisseur nvarchar(20) NOT NULL DEFAULT '409000',
                -- Comptes TVA
                CompteTVACollectee nvarchar(20) NOT NULL DEFAULT '445710',
                CompteTVADeductible nvarchar(20) NOT NULL DEFAULT '445660',
                CompteTVASurEncaissements nvarchar(20) NOT NULL DEFAULT '445720',
                -- Comptes produits / charges
                CompteVenteMarchandises nvarchar(20) NOT NULL DEFAULT '707000',
                CompteVentePrestations nvarchar(20) NOT NULL DEFAULT '706000',
                CompteAchatMarchandises nvarchar(20) NOT NULL DEFAULT '607000',
                CompteAchatMatieres nvarchar(20) NOT NULL DEFAULT '601000',
                CompteFraisPort nvarchar(20) NOT NULL DEFAULT '624100',
                CompteRemiseAccordee nvarchar(20) NOT NULL DEFAULT '709000',
                CompteRemiseObtenue nvarchar(20) NOT NULL DEFAULT '609000',
                -- Journaux comptables
                JournalVentes nvarchar(10) NOT NULL DEFAULT 'VT',
                JournalAchats nvarchar(10) NOT NULL DEFAULT 'AC',
                JournalBanque nvarchar(10) NOT NULL DEFAULT 'BQ',
                JournalCaisse nvarchar(10) NOT NULL DEFAULT 'CA',
                JournalOD nvarchar(10) NOT NULL DEFAULT 'OD',
                JournalANouveaux nvarchar(10) NOT NULL DEFAULT 'AN',
                -- Labels journaux
                JournalVentesLibelle nvarchar(50) NOT NULL DEFAULT 'Journal des ventes',
                JournalAchatsLibelle nvarchar(50) NOT NULL DEFAULT 'Journal des achats',
                JournalBanqueLibelle nvarchar(50) NOT NULL DEFAULT 'Journal de banque',
                JournalCaisseLibelle nvarchar(50) NOT NULL DEFAULT 'Journal de caisse',
                JournalODLibelle nvarchar(50) NOT NULL DEFAULT 'Opérations diverses',
                JournalANouveauxLibelle nvarchar(50) NOT NULL DEFAULT 'À nouveaux',
                -- Paramètres généraux
                RegimeTVA int NOT NULL DEFAULT 1, -- 1=Normal 2=Encaissements 3=Franchise
                FormatExportCompta nvarchar(20) NOT NULL DEFAULT 'SAGE',
                UpdatedAt datetime2 NULL, UpdatedBy nvarchar(450) NULL
            )", conn);
        await cmd.ExecuteNonQueryAsync();

        // Seed ligne par défaut
        await using var seed = new SqlCommand(@"
            IF NOT EXISTS (SELECT 1 FROM ParametresComptables WHERE Id=1)
            INSERT INTO ParametresComptables (Id) VALUES (1)", conn);
        await seed.ExecuteNonQueryAsync();
    }

    [HttpGet("comptabilite")]
    public async Task<IActionResult> GetComptabilite()
    {
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            await EnsureComptabiliteTableAsync(conn);
            await using var cmd = new SqlCommand("SELECT * FROM ParametresComptables WHERE Id=1", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return Ok(new ComptabiliteDto());
            return Ok(new ComptabiliteDto {
                CompteClientDefaut=r["CompteClientDefaut"].ToString()!,
                CompteClientEtranger=r["CompteClientEtranger"].ToString()!,
                CompteClientDouteux=r["CompteClientDouteux"].ToString()!,
                CompteAcompteClient=r["CompteAcompteClient"].ToString()!,
                CompteFournisseurDefaut=r["CompteFournisseurDefaut"].ToString()!,
                CompteFournisseurEtranger=r["CompteFournisseurEtranger"].ToString()!,
                CompteAcompteFournisseur=r["CompteAcompteFournisseur"].ToString()!,
                CompteTVACollectee=r["CompteTVACollectee"].ToString()!,
                CompteTVADeductible=r["CompteTVADeductible"].ToString()!,
                CompteTVASurEncaissements=r["CompteTVASurEncaissements"].ToString()!,
                CompteVenteMarchandises=r["CompteVenteMarchandises"].ToString()!,
                CompteVentePrestations=r["CompteVentePrestations"].ToString()!,
                CompteAchatMarchandises=r["CompteAchatMarchandises"].ToString()!,
                CompteAchatMatieres=r["CompteAchatMatieres"].ToString()!,
                CompteFraisPort=r["CompteFraisPort"].ToString()!,
                CompteRemiseAccordee=r["CompteRemiseAccordee"].ToString()!,
                CompteRemiseObtenue=r["CompteRemiseObtenue"].ToString()!,
                JournalVentes=r["JournalVentes"].ToString()!,
                JournalAchats=r["JournalAchats"].ToString()!,
                JournalBanque=r["JournalBanque"].ToString()!,
                JournalCaisse=r["JournalCaisse"].ToString()!,
                JournalOD=r["JournalOD"].ToString()!,
                JournalANouveaux=r["JournalANouveaux"].ToString()!,
                JournalVentesLibelle=r["JournalVentesLibelle"].ToString()!,
                JournalAchatsLibelle=r["JournalAchatsLibelle"].ToString()!,
                JournalBanqueLibelle=r["JournalBanqueLibelle"].ToString()!,
                JournalCaisseLibelle=r["JournalCaisseLibelle"].ToString()!,
                JournalODLibelle=r["JournalODLibelle"].ToString()!,
                JournalANouveauxLibelle=r["JournalANouveauxLibelle"].ToString()!,
                RegimeTVA=Convert.ToInt32(r["RegimeTVA"]),
                FormatExportCompta=r["FormatExportCompta"].ToString()!
            });
        }
        catch (Exception ex) { return StatusCode(500, new { succes=false, message=ex.Message }); }
    }

    [HttpPost("comptabilite")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> SaveComptabilite([FromBody] ComptabiliteDto dto)
    {
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            await EnsureComptabiliteTableAsync(conn);
            await using var cmd = new SqlCommand(@"
                UPDATE ParametresComptables SET
                    CompteClientDefaut=@c1, CompteClientEtranger=@c2, CompteClientDouteux=@c3, CompteAcompteClient=@c4,
                    CompteFournisseurDefaut=@c5, CompteFournisseurEtranger=@c6, CompteAcompteFournisseur=@c7,
                    CompteTVACollectee=@c8, CompteTVADeductible=@c9, CompteTVASurEncaissements=@c10,
                    CompteVenteMarchandises=@c11, CompteVentePrestations=@c12, CompteAchatMarchandises=@c13,
                    CompteAchatMatieres=@c14, CompteFraisPort=@c15, CompteRemiseAccordee=@c16, CompteRemiseObtenue=@c17,
                    JournalVentes=@j1, JournalAchats=@j2, JournalBanque=@j3, JournalCaisse=@j4,
                    JournalOD=@j5, JournalANouveaux=@j6,
                    JournalVentesLibelle=@jl1, JournalAchatsLibelle=@jl2, JournalBanqueLibelle=@jl3,
                    JournalCaisseLibelle=@jl4, JournalODLibelle=@jl5, JournalANouveauxLibelle=@jl6,
                    RegimeTVA=@rtva, FormatExportCompta=@fmt, UpdatedAt=GETUTCDATE(), UpdatedBy=@user
                WHERE Id=1", conn);
            cmd.Parameters.AddWithValue("@c1", dto.CompteClientDefaut);
            cmd.Parameters.AddWithValue("@c2", dto.CompteClientEtranger);
            cmd.Parameters.AddWithValue("@c3", dto.CompteClientDouteux);
            cmd.Parameters.AddWithValue("@c4", dto.CompteAcompteClient);
            cmd.Parameters.AddWithValue("@c5", dto.CompteFournisseurDefaut);
            cmd.Parameters.AddWithValue("@c6", dto.CompteFournisseurEtranger);
            cmd.Parameters.AddWithValue("@c7", dto.CompteAcompteFournisseur);
            cmd.Parameters.AddWithValue("@c8", dto.CompteTVACollectee);
            cmd.Parameters.AddWithValue("@c9", dto.CompteTVADeductible);
            cmd.Parameters.AddWithValue("@c10", dto.CompteTVASurEncaissements);
            cmd.Parameters.AddWithValue("@c11", dto.CompteVenteMarchandises);
            cmd.Parameters.AddWithValue("@c12", dto.CompteVentePrestations);
            cmd.Parameters.AddWithValue("@c13", dto.CompteAchatMarchandises);
            cmd.Parameters.AddWithValue("@c14", dto.CompteAchatMatieres);
            cmd.Parameters.AddWithValue("@c15", dto.CompteFraisPort);
            cmd.Parameters.AddWithValue("@c16", dto.CompteRemiseAccordee);
            cmd.Parameters.AddWithValue("@c17", dto.CompteRemiseObtenue);
            cmd.Parameters.AddWithValue("@j1", dto.JournalVentes);
            cmd.Parameters.AddWithValue("@j2", dto.JournalAchats);
            cmd.Parameters.AddWithValue("@j3", dto.JournalBanque);
            cmd.Parameters.AddWithValue("@j4", dto.JournalCaisse);
            cmd.Parameters.AddWithValue("@j5", dto.JournalOD);
            cmd.Parameters.AddWithValue("@j6", dto.JournalANouveaux);
            cmd.Parameters.AddWithValue("@jl1", dto.JournalVentesLibelle);
            cmd.Parameters.AddWithValue("@jl2", dto.JournalAchatsLibelle);
            cmd.Parameters.AddWithValue("@jl3", dto.JournalBanqueLibelle);
            cmd.Parameters.AddWithValue("@jl4", dto.JournalCaisseLibelle);
            cmd.Parameters.AddWithValue("@jl5", dto.JournalODLibelle);
            cmd.Parameters.AddWithValue("@jl6", dto.JournalANouveauxLibelle);
            cmd.Parameters.AddWithValue("@rtva", dto.RegimeTVA);
            cmd.Parameters.AddWithValue("@fmt", dto.FormatExportCompta);
            cmd.Parameters.AddWithValue("@user", UserId);
            await cmd.ExecuteNonQueryAsync();
            return Ok(new { succes=true, message="Paramètres comptables enregistrés." });
        }
        catch (Exception ex) { return StatusCode(500, new { succes=false, message=ex.Message }); }
    }
}

public class ComptabiliteDto
{
    public string CompteClientDefaut { get; set; } = "411000";
    public string CompteClientEtranger { get; set; } = "411100";
    public string CompteClientDouteux { get; set; } = "416000";
    public string CompteAcompteClient { get; set; } = "419000";
    public string CompteFournisseurDefaut { get; set; } = "401000";
    public string CompteFournisseurEtranger { get; set; } = "401100";
    public string CompteAcompteFournisseur { get; set; } = "409000";
    public string CompteTVACollectee { get; set; } = "445710";
    public string CompteTVADeductible { get; set; } = "445660";
    public string CompteTVASurEncaissements { get; set; } = "445720";
    public string CompteVenteMarchandises { get; set; } = "707000";
    public string CompteVentePrestations { get; set; } = "706000";
    public string CompteAchatMarchandises { get; set; } = "607000";
    public string CompteAchatMatieres { get; set; } = "601000";
    public string CompteFraisPort { get; set; } = "624100";
    public string CompteRemiseAccordee { get; set; } = "709000";
    public string CompteRemiseObtenue { get; set; } = "609000";
    public string JournalVentes { get; set; } = "VT";
    public string JournalAchats { get; set; } = "AC";
    public string JournalBanque { get; set; } = "BQ";
    public string JournalCaisse { get; set; } = "CA";
    public string JournalOD { get; set; } = "OD";
    public string JournalANouveaux { get; set; } = "AN";
    public string JournalVentesLibelle { get; set; } = "Journal des ventes";
    public string JournalAchatsLibelle { get; set; } = "Journal des achats";
    public string JournalBanqueLibelle { get; set; } = "Journal de banque";
    public string JournalCaisseLibelle { get; set; } = "Journal de caisse";
    public string JournalODLibelle { get; set; } = "Opérations diverses";
    public string JournalANouveauxLibelle { get; set; } = "À nouveaux";
    public int RegimeTVA { get; set; } = 1;
    public string FormatExportCompta { get; set; } = "SAGE";
}
