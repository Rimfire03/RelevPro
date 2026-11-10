# RelevPro

Application Windows de relevés de compteurs et de tâches périodiques.

- Compteurs partagés entre plusieurs vues de relevé ; lignes = compteur ou tâche à cocher, avec récurrence (hebdomadaire, mensuelle, trimestrielle, semestrielle, annuelle).
- Contrôle des saisies : un compteur ne peut qu'augmenter, alerte en cas d'écart trop grand, valeur forçable.
- Récapitulatif par vue : consommations par mois et par an. Export Excel. Centre de notifications.
- Base SQLite ou serveur (PostgreSQL, MariaDB, SQL Server), multisite, licence, sauvegardes, mises à jour.

Génération : voir tools/Build-Quiet.ps1 (variable TOMLINE_NUGET_TOKEN requise pour les paquets TomLine.*).
