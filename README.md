# Assistance RAIDBOX

Depot de migration et de mise a jour de l'application Assistance RAIDBOX.

## Production

Version stable publiee : `1.39`.

Les anciennes installations executent `teamviewer.bat`. Le lanceur tente de
recuperer le relais actuel avec le Git embarque, puis utilise le telechargement
direct de la release si Git est absent ou en erreur.

Depuis la version 1.37 de ce depot, `TeamViewerQS.exe` est un programme de
transition qui :

1. telecharge directement l'installeur `1.39` depuis la release GitHub `v1.39` ;
2. verifie son SHA-256 ;
3. installe Assistance RAIDBOX silencieusement ;
4. configure `latest.json` comme manifeste de mise a jour GitHub ;
5. ferme l'instance provisoire ouverte par l'installeur ;
6. lance une seule instance de la nouvelle application.

Une fois la version installee lancee, le manifeste `latest.json` met
automatiquement l'application a jour vers la version stable `1.39`.

Depuis la version 1.36, les scripts PowerShell de l'application sont embarques
dans `AssistanceRaidbox.exe`. Le demarrage ne depend donc plus de la politique
d'execution des fichiers `.ps1` de Windows.

La migration 1.38 supprime l'ancienne application dans `C:\ProgramData\R@IDBOX`
mais conserve integralement les dossiers `background` et `icon`.

La version 1.39 utilise exclusivement les deux modules personnalises RAIDBOX
fournis par TeamViewer :

- assistance principale : `https://get.teamviewer.com/jaj7m8a` ;
- assistance secondaire : `https://get.teamviewer.com/6f482af`.

Le build refuse un autre fichier, meme renomme, en controlant les SHA-256
attendus et la signature `TeamViewer Germany GmbH`.

Le code source du programme de transition est dans `MigrationTo133.cs`
(nom historique conserve pour la tracabilite).

## Retour arriere

Etat Git avant la migration :

```text
backup-before-1.39-production-20260724
```
