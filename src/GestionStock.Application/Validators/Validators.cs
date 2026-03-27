using FluentValidation;
using GestionStock.Application.DTOs;

namespace GestionStock.Application.Validators;

public class CreerArticleValidator : AbstractValidator<CreerArticleDto>
{
    public CreerArticleValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Le code article est obligatoire.")
            .MaximumLength(20).WithMessage("Le code ne peut pas dépasser 20 caractères.")
            .Matches("^[A-Z0-9_-]+$").WithMessage("Le code ne peut contenir que des lettres majuscules, chiffres, tirets et underscores.");

        RuleFor(x => x.Designation)
            .NotEmpty().WithMessage("La désignation est obligatoire.")
            .MinimumLength(3).WithMessage("La désignation doit contenir au moins 3 caractères.")
            .MaximumLength(200).WithMessage("La désignation ne peut pas dépasser 200 caractères.");

        RuleFor(x => x.Categorie)
            .NotEmpty().WithMessage("La catégorie est obligatoire.");

        RuleFor(x => x.Unite)
            .NotEmpty().WithMessage("L'unité est obligatoire.");

        RuleFor(x => x.PrixAchat)
            .GreaterThanOrEqualTo(0).WithMessage("Le prix d'achat ne peut pas être négatif.");

        RuleFor(x => x.SeuilAlerte)
            .GreaterThanOrEqualTo(0).WithMessage("Le seuil d'alerte ne peut pas être négatif.");

        RuleFor(x => x.StockMinimum)
            .GreaterThanOrEqualTo(0).WithMessage("Le stock minimum ne peut pas être négatif.");

        RuleFor(x => x.StockMaximum)
            .GreaterThanOrEqualTo(0).WithMessage("Le stock maximum ne peut pas être négatif.")
            .Must((dto, max) => max == 0 || max > dto.StockMinimum)
            .WithMessage("Le stock maximum doit être supérieur au stock minimum (ou 0 = illimité).");
    }
}

public class EntreeStockValidator : AbstractValidator<EntreeStockDto>
{
    public EntreeStockValidator()
    {
        RuleFor(x => x.ArticleId)
            .NotEmpty().WithMessage("L'article est obligatoire.");

        RuleFor(x => x.EmplacementId)
            .NotEmpty().WithMessage("L'emplacement est obligatoire.");

        RuleFor(x => x.Quantite)
            .GreaterThan(0).WithMessage("La quantité doit être supérieure à 0.");

        RuleFor(x => x.PrixUnitaire)
            .GreaterThanOrEqualTo(0).WithMessage("Le prix unitaire ne peut pas être négatif.");

        RuleFor(x => x.Reference)
            .NotEmpty().WithMessage("La référence est obligatoire (N° BL, N° commande...).");
    }
}

public class SortieStockValidator : AbstractValidator<SortieStockDto>
{
    public SortieStockValidator()
    {
        RuleFor(x => x.ArticleId).NotEmpty();
        RuleFor(x => x.EmplacementId).NotEmpty();
        RuleFor(x => x.Quantite).GreaterThan(0);
        RuleFor(x => x.Reference).NotEmpty();
    }
}

public class CreerCommandeValidator : AbstractValidator<CreerCommandeAchatDto>
{
    public CreerCommandeValidator()
    {
        RuleFor(x => x.FournisseurId)
            .NotEmpty().WithMessage("Le fournisseur est obligatoire.");

        RuleFor(x => x.DateLivraisonPrevue)
            .GreaterThan(DateTime.Now)
            .WithMessage("La date de livraison prévue doit être dans le futur.");

        RuleFor(x => x.Lignes)
            .NotEmpty().WithMessage("La commande doit contenir au moins une ligne.")
            .Must(l => l.All(li => li.Quantite > 0))
            .WithMessage("Toutes les lignes doivent avoir une quantité positive.");
    }
}

public class CreerFournisseurValidator : AbstractValidator<CreerFournisseurDto>
{
    public CreerFournisseurValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Le code fournisseur est obligatoire.")
            .MaximumLength(10);

        RuleFor(x => x.RaisonSociale)
            .NotEmpty().WithMessage("La raison sociale est obligatoire.")
            .MaximumLength(200);

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("L'email est obligatoire.")
            .EmailAddress().WithMessage("Format d'email invalide.");

        RuleFor(x => x.Telephone)
            .NotEmpty().WithMessage("Le téléphone est obligatoire.");

        RuleFor(x => x.DelaiLivraisonJours)
            .GreaterThanOrEqualTo(0).WithMessage("Le délai de livraison ne peut pas être négatif.");
    }
}
