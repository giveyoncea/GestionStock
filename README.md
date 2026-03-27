# 📦 GestionStock – Système WMS/SCM

Application de **Gestion de Stock et Approvisionnements** développée en **ASP.NET Core 8** avec
architecture **Clean Architecture**, conforme au cahier des charges fourni.

---

## 🏗️ Architecture

```
GestionStock/
├── src/
│   ├── GestionStock.Domain/           ← Entités, énumérations, interfaces de repository
│   │   ├── Entities/                  ← Article, Stock, Lot, Fournisseur, Commande...
│   │   ├── Enums/                     ← TypeMouvement, StatutCommande, RoleUtilisateur...
│   │   └── Interfaces/                ← IRepository<T>, IUnitOfWork
│   │
│   ├── GestionStock.Application/      ← Logique métier, DTOs, services, validateurs
│   │   ├── DTOs/                      ← Tous les Data Transfer Objects
│   │   ├── Interfaces/                ← IArticleService, IStockService...
│   │   ├── Services/                  ← ArticleService, StockService, CommandeAchatService...
│   │   └── Validators/                ← Règles FluentValidation
│   │
│   ├── GestionStock.Infrastructure/   ← EF Core, SQL Server, Identity, Repositories
│   │   ├── Data/                      ← AppDbContext (EF Core + Identity)
│   │   ├── Repositories/              ← Implémentation des repositories
│   │   ├── Identity/                  ← ApplicationUser (ASP.NET Identity étendu)
│   │   ├── Services/                  ← AuthService (JWT)
│   │   ├── Migrations/                ← Migrations EF Core
│   │   └── UnitOfWork.cs
│   │
│   └── GestionStock.API/              ← Couche présentation (REST API)
│       ├── Controllers/               ← Auth, Dashboard, Articles, Stocks, Fournisseurs, Commandes
│       ├── Middleware/                ← Gestion des exceptions, Audit IP
│       ├── Extensions/                ← Injection de dépendances, Swagger, CORS, JWT
│       ├── Program.cs
│       └── appsettings.json
```

---

## 🚀 Démarrage rapide

### Prérequis
- **.NET 8 SDK** : https://dot.net/download
- **SQL Server** (LocalDB, Express ou complet) ou **SQL Server Developer**
- **Visual Studio 2022** (recommandé) ou VS Code + extension C#

### 1. Cloner et ouvrir la solution

```bash
# Ouvrir GestionStock.sln dans Visual Studio 2022
# Ou via CLI :
cd GestionStock
dotnet restore
```

### 2. Configurer la connexion SQL Server

Modifier `src/GestionStock.API/appsettings.json` :

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=GestionStockDB;Trusted_Connection=True;TrustServerCertificate=True"
  }
}
```

> Pour SQL Server Express : `Server=.\\SQLEXPRESS;Database=GestionStockDB;...`

### 3. Créer et migrer la base de données

```bash
# Depuis le répertoire racine de la solution
dotnet ef database update \
  --project src/GestionStock.Infrastructure \
  --startup-project src/GestionStock.API

# Ou dans la Console du Gestionnaire de Packages (Visual Studio) :
# Update-Database
```

> **Note :** Si c'est la première migration à créer :
> ```bash
> dotnet ef migrations add InitialCreate \
>   --project src/GestionStock.Infrastructure \
>   --startup-project src/GestionStock.API
> ```

### 4. Lancer l'API

```bash
cd src/GestionStock.API
dotnet run
```

L'API démarre sur `https://localhost:7000`  
**Swagger UI** disponible sur `https://localhost:7000` (à la racine)

---

## 🔐 Authentification

### Compte administrateur par défaut
| Email | Mot de passe |
|-------|-------------|
| admin@gestionstock.com | Admin@2024!Stock |

### Flux d'authentification JWT
```
POST /api/auth/login
Body: { "email": "...", "motDePasse": "..." }

→ Réponse: { "token": "eyJhbGci...", "expiration": "...", "role": "Admin" }
```

Utiliser le token dans les en-têtes HTTP :
```
Authorization: Bearer eyJhbGci...
```

---

## 📋 Endpoints principaux

### 🔑 Authentification
| Méthode | Endpoint | Description |
|---------|----------|-------------|
| POST | `/api/auth/login` | Connexion – retourne JWT |
| POST | `/api/auth/inscription` | Créer un compte (Admin) |

### 📊 Tableau de bord
| Méthode | Endpoint | Description |
|---------|----------|-------------|
| GET | `/api/dashboard` | KPIs, alertes, mouvements récents |

### 📦 Articles
| Méthode | Endpoint | Description |
|---------|----------|-------------|
| GET | `/api/articles?page=1&pageSize=20&search=...` | Liste paginée |
| GET | `/api/articles/{id}` | Détail |
| GET | `/api/articles/barcode/{code}` | Recherche code-barres |
| GET | `/api/articles/alertes` | Articles en alerte/rupture |
| GET | `/api/articles/categories` | Liste des catégories |
| POST | `/api/articles` | Créer (Magasinier+) |
| PUT | `/api/articles/{id}` | Modifier (Magasinier+) |
| DELETE | `/api/articles/{id}` | Désactiver (Admin) |

### 📊 Stocks & Mouvements
| Méthode | Endpoint | Description |
|---------|----------|-------------|
| GET | `/api/stocks` | Résumé des stocks |
| GET | `/api/stocks/article/{id}` | Stock par emplacement |
| GET | `/api/stocks/mouvements?du=...&au=...` | Historique |
| POST | `/api/stocks/entree` | Entrée en stock |
| POST | `/api/stocks/sortie` | Sortie de stock |
| POST | `/api/stocks/transfert` | Transfert inter-emplacements |

### 🏭 Fournisseurs
| Méthode | Endpoint | Description |
|---------|----------|-------------|
| GET | `/api/fournisseurs` | Liste |
| POST | `/api/fournisseurs` | Créer (Acheteur+) |
| PUT | `/api/fournisseurs/{id}` | Modifier |

### 🛒 Commandes d'Achat
| Méthode | Endpoint | Description |
|---------|----------|-------------|
| GET | `/api/commandesachat` | Liste |
| GET | `/api/commandesachat/en-attente` | Commandes à réceptionner |
| POST | `/api/commandesachat` | Créer (Acheteur+) |
| POST | `/api/commandesachat/{id}/valider` | Valider |
| POST | `/api/commandesachat/reception` | Réceptionner → entrée stock auto |
| POST | `/api/commandesachat/{id}/annuler` | Annuler |

---

## 🔒 Rôles et permissions

| Rôle | Permissions |
|------|-------------|
| **Admin** | Accès total, gestion des comptes |
| **Superviseur** | Lecture + validation, rapports |
| **Magasinier** | Entrées/sorties/transferts de stock |
| **Acheteur** | Gestion fournisseurs et commandes d'achat |
| **Lecteur** | Consultation uniquement |

---

## 🧪 Exemples d'appels API

### Créer un article
```json
POST /api/articles
{
  "code": "ART-001",
  "codeBarres": "3612345678901",
  "designation": "Vis M6x20 inox",
  "description": "Vis à tête hexagonale M6x20 en acier inoxydable",
  "categorie": "Visserie",
  "familleArticle": "Fixation",
  "unite": "PCS",
  "prixAchat": 0.25,
  "prixVente": 0.45,
  "seuilAlerte": 100,
  "stockMinimum": 50,
  "stockMaximum": 2000,
  "gestionLot": false,
  "gestionDLUO": false
}
```

### Entrée en stock
```json
POST /api/stocks/entree
{
  "articleId": "...",
  "emplacementId": "...",
  "quantite": 500,
  "prixUnitaire": 0.24,
  "reference": "BL-2024-001234",
  "motif": "Réception commande CA-202401-0001"
}
```

### Créer une commande d'achat
```json
POST /api/commandesachat
{
  "fournisseurId": "...",
  "dateLivraisonPrevue": "2024-03-15T00:00:00Z",
  "lignes": [
    {
      "articleId": "...",
      "quantite": 1000,
      "prixUnitaire": 0.22
    }
  ]
}
```

---

## 📁 Structure de la base de données

| Table | Description |
|-------|-------------|
| `Articles` | Catalogue articles (code, désignation, seuils...) |
| `Stocks` | Stock par article × emplacement |
| `Emplacements` | Zones/allées/niveaux de l'entrepôt |
| `MouvementsStock` | Journal horodaté de tous les mouvements |
| `Lots` | Numéros de lot avec DLC/DLUO |
| `Fournisseurs` | Référentiel fournisseurs |
| `CommandesAchat` | Bons de commande fournisseurs |
| `LignesCommandeAchat` | Détail des lignes de commande |
| `Inventaires` | Sessions d'inventaire |
| `LignesInventaire` | Comptage physique vs théorique |
| `AuditTrails` | Journal d'audit complet |
| `AspNetUsers` | Utilisateurs (ASP.NET Identity) |
| `AspNetRoles` | Rôles (Admin, Magasinier...) |

---

## 🔧 Technologies utilisées

| Couche | Technologie |
|--------|-------------|
| Framework | ASP.NET Core 8 |
| ORM | Entity Framework Core 8 |
| Base de données | SQL Server (LocalDB/Express/Full) |
| Authentification | ASP.NET Core Identity + JWT Bearer |
| Validation | FluentValidation 11 |
| Documentation | Swagger / OpenAPI (Swashbuckle) |
| Logging | Serilog (Console + Fichier rotatif) |
| Architecture | Clean Architecture (Domain / Application / Infrastructure / API) |
| Patron de conception | Repository + Unit of Work |

---

## 📈 Feuille de route (extensions possibles)

- [ ] **Frontend** : Blazor WebAssembly ou Angular/React
- [ ] **Rapports PDF** : RDLC / FastReport / QuestPDF
- [ ] **Scan mobile** : Interface optimisée douchette (NF-ERG-03)
- [ ] **Notifications** : SignalR pour alertes temps réel
- [ ] **Export Excel** : ClosedXML
- [ ] **EDI / Intégration ERP** : NF-INT-01
- [ ] **Supervision** : Health checks + Prometheus + Grafana
- [ ] **Tests** : xUnit + Moq + TestContainers

---

*Application conforme au Cahier des Charges WMS/SCM – Exigences fonctionnelles et non-fonctionnelles couvertes : NF-SEC-01/02/03, NF-TRACE-01, NF-INT-01, NF-ERG-01/03, NF-GS-03.*
