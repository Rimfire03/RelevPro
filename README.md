# RelevPro

[![Release](https://img.shields.io/github/v/release/Rimfire03/RelevPro)](https://github.com/Rimfire03/RelevPro/releases/latest) [![Téléchargements](https://img.shields.io/github/downloads/Rimfire03/RelevPro/total)](https://github.com/Rimfire03/RelevPro/releases) ![.NET 8](https://img.shields.io/badge/.NET-8-512BD4?logo=dotnet&logoColor=white) ![WPF](https://img.shields.io/badge/UI-WPF-5C2D91) ![Windows](https://img.shields.io/badge/Windows-10%2F11-0078D6?logo=windows&logoColor=white)

Application Windows de relevés de compteurs et de tâches périodiques.

- Compteurs partagés entre plusieurs vues de relevé ; lignes = compteur ou tâche à cocher, avec récurrence (hebdomadaire, mensuelle, trimestrielle, semestrielle, annuelle).
- Contrôle des saisies : un compteur ne peut qu'augmenter, alerte en cas d'écart trop grand, valeur forçable.
- Récapitulatif par vue : consommations par mois et par an. Export Excel. Centre de notifications.
- Base SQLite ou serveur (PostgreSQL, MariaDB, SQL Server), multisite, licence, sauvegardes, mises à jour.

Génération : voir tools/Build-Quiet.ps1 (variable TOMLINE_NUGET_TOKEN requise pour les paquets TomLine.*).
