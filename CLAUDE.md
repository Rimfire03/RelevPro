# Consignes pour Claude

## Git

- Toujours committer et pousser directement sur la branche `main` (consigne permanente du propriétaire). Pas de branche de
  travail ni de pull request sauf demande explicite. Avant de modifier : synchroniser avec `main` (fast-forward uniquement).

## Projet

RelevPro : relevés de compteurs et petites tâches (WPF .NET 8, EF Core, SQLite ou serveur). Même socle que l'application
Filtres (licence TomLine.Licensing, multisite, sauvegardes, accès en écriture, mises à jour). Domaine dans
`src/RelevPro.Core/Models/Reading.cs`, contrôles dans `ReadingValidator`, notifications dans `NotificationService`.
Produit de licence : slug `relevpro` (à créer côté serveur de licences).

## Release

Voir `tools\Release.ps1` (incrémenter `<Version>` dans `RelevPro.csproj`, committer, pousser, puis lancer le script). L'exe
publié doit être signé avec le certificat global « TomLine prod&co » (`tools\Sign-Release.ps1`), jamais de .pfx commité.
