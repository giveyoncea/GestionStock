namespace GestionStock.Domain.Enums;

public enum StatutArticle
{
    Actif = 1,
    Inactif = 2,
    Obsolete = 3
}

public enum TypeMouvement
{
    Entree = 1,         // Réception fournisseur
    Sortie = 2,         // Expédition / consommation
    Transfert = 3,      // Entre emplacements
    Ajustement = 4,     // Correction d'inventaire
    Retour = 5,         // Retour fournisseur
    Perte = 6,          // Casse / perte
    Production = 7      // Entrée de production
}

public enum StatutCommande
{
    Brouillon = 1,
    Soumise = 2,
    Confirmee = 3,
    EnCours = 4,
    Recue = 5,
    ReceptionPartielle = 6,
    Annulee = 7,
    Cloturee = 8
}

public enum TypeEmplacement
{
    Standard = 1,
    Quai = 2,
    Frigo = 3,
    Congelateur = 4,
    MatieresHazardeueses = 5,
    Stockage = 6,
    Picking = 7,
    Retour = 8,
    Quarantaine = 9
}

public enum StatutFournisseur
{
    Actif = 1,
    Inactif = 2,
    Blackliste = 3,
    EnEvaluation = 4
}

public enum StatutLot
{
    Disponible = 1,
    Quarantaine = 2,
    Epuise = 3,
    Perime = 4,
    Bloque = 5
}

public enum TypeInventaire
{
    Total = 1,          // Inventaire annuel complet
    Tournant = 2,       // Par zone / rotation ABC
    Partiel = 3         // Sur sélection d'articles
}

public enum StatutInventaire
{
    EnCours = 1,
    EnAttente = 2,
    Valide = 3,
    Annule = 4
}

public enum RoleUtilisateur
{
    Admin = 1,
    Magasinier = 2,
    Acheteur = 3,
    Superviseur = 4,
    Lecteur = 5
}
