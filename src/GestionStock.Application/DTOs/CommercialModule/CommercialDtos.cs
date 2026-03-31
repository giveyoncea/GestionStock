namespace GestionStock.Application.DTOs;

public record CommercialClientDto(
    Guid Id,
    string Code,
    string RaisonSociale,
    int TypeClient,
    string? Email,
    string? Telephone,
    string? Ville,
    decimal TauxRemise,
    int DelaiPaiementJours,
    decimal PlafondCredit,
    bool EstActif,
    DateTime CreatedAt,
    decimal Encours
);

public record CommercialClientRequestDto(
    string RaisonSociale,
    int TypeClient,
    string? Email,
    string? Telephone,
    string? Adresse,
    string? CodePostal,
    string? Ville,
    string? Pays,
    string? NumeroTVA,
    string? Siret,
    int DelaiPaiementJours,
    decimal TauxRemise,
    decimal PlafondCredit,
    string? Notes,
    bool EstActif
);

public record CommercialVenteListItemDto(
    Guid Id,
    string Numero,
    int TypeDocument,
    string TypeLibelle,
    int Statut,
    string StatutLibelle,
    DateTime DateDocument,
    DateTime? DateEcheance,
    string ClientNom,
    Guid ClientId,
    decimal MontantHT,
    decimal MontantTVA,
    decimal MontantTTC,
    decimal MontantRegle,
    decimal Solde,
    bool EstVerrouille,
    DateTime CreatedAt,
    decimal FraisLivraison,
    decimal MontantAcompte
);

public record CommercialVenteDocumentDto(
    Guid Id,
    string Numero,
    int TypeDocument,
    string TypeLibelle,
    int Statut,
    string StatutLibelle,
    Guid ClientId,
    string ClientNom,
    DateTime DateDocument,
    DateTime? DateEcheance,
    string? AdresseLivraison,
    decimal MontantHT,
    decimal MontantRemise,
    decimal MontantTVA,
    decimal MontantTTC,
    decimal FraisLivraison,
    decimal MontantAcompte,
    decimal MontantRegle,
    bool EstVerrouille,
    string? NotesInternes,
    string? NotesExterne,
    Guid? DocumentParentId
);

public record CommercialVenteLigneDto(
    Guid Id,
    Guid DocumentId,
    Guid ArticleId,
    string Designation,
    decimal Quantite,
    decimal QuantiteLivree,
    decimal PrixUnitaireHT,
    decimal TauxRemise,
    decimal MontantRemise,
    decimal PrixNetHT,
    decimal TauxTVA,
    decimal MontantTVA,
    decimal MontantTTC,
    string? NumeroLot,
    string? NumeroSerie,
    int Ordre,
    string? ArticleCode
);

public record CommercialVenteDetailDto(
    CommercialVenteDocumentDto Document,
    IReadOnlyList<CommercialVenteLigneDto> Lignes
);

public record CommercialVenteLigneRequestDto(
    Guid ArticleId,
    string Designation,
    decimal Quantite,
    decimal PrixUnitaireHT,
    decimal TauxRemise,
    decimal TauxTVA,
    string? NumeroLot,
    string? NumeroSerie
);

public record CommercialVenteRequestDto(
    int TypeDocument,
    Guid ClientId,
    Guid? RepresentantId,
    Guid? DocumentParentId,
    DateTime? DateDocument,
    DateTime? DateEcheance,
    DateTime? DateLivraisonPrevue,
    string? AdresseLivraison,
    Guid? DepotId,
    decimal FraisLivraison,
    decimal MontantAcompte,
    decimal TauxTVA,
    string? ConditionsPaiement,
    string? NotesInternes,
    string? NotesExterne,
    IReadOnlyList<CommercialVenteLigneRequestDto> Lignes
);

public record CommercialReglementRequestDto(
    decimal Montant,
    int ModeReglement,
    DateTime? DateReglement,
    string? Reference,
    string? Notes
);

public record CommercialAchatListItemDto(
    Guid Id,
    string Numero,
    int TypeDocument,
    string TypeLibelle,
    int Statut,
    string StatutLibelle,
    DateTime DateDocument,
    DateTime? DateLivraisonPrevue,
    string FournisseurNom,
    decimal MontantTTC,
    decimal MontantRegle,
    decimal Solde,
    bool EstVerrouille
);

public record CommercialAchatDocumentDto(
    Guid Id,
    string Numero,
    int TypeDocument,
    string TypeLibelle,
    int Statut,
    string StatutLibelle,
    Guid FournisseurId,
    string FournisseurNom,
    DateTime DateDocument,
    DateTime? DateLivraisonPrevue,
    decimal MontantHT,
    decimal MontantTVA,
    decimal MontantTTC,
    decimal FraisLivraison,
    decimal MontantRegle,
    bool EstVerrouille,
    string? NotesInternes,
    Guid? DocumentParentId
);

public record CommercialAchatLigneDto(
    Guid Id,
    Guid DocumentId,
    Guid ArticleId,
    string Designation,
    decimal Quantite,
    decimal QuantiteLivree,
    decimal PrixUnitaireHT,
    decimal TauxRemise,
    decimal MontantRemise,
    decimal PrixNetHT,
    decimal TauxTVA,
    decimal MontantTTC,
    string? NumeroLot,
    string? NumeroSerie,
    int Ordre,
    string? ArticleCode
);

public record CommercialAchatDetailDto(
    CommercialAchatDocumentDto Document,
    IReadOnlyList<CommercialAchatLigneDto> Lignes
);

public record CommercialAchatLigneRequestDto(
    Guid ArticleId,
    string Designation,
    decimal Quantite,
    decimal PrixUnitaireHT,
    decimal TauxRemise,
    decimal TauxTVA,
    string? NumeroLot,
    string? NumeroSerie
);

public record CommercialAchatRequestDto(
    int TypeDocument,
    Guid FournisseurId,
    Guid? DocumentParentId,
    DateTime? DateDocument,
    DateTime? DateLivraisonPrevue,
    Guid? DepotId,
    decimal FraisLivraison,
    string? NotesInternes,
    IReadOnlyList<CommercialAchatLigneRequestDto> Lignes
);
