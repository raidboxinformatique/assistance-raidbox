# Assistance RAIDBOX

Depot de migration et de mise a jour de l'application Assistance RAIDBOX.

## Production

Version stable publiee : `1.36`.

Les anciennes installations executent `teamviewer.bat`, qui effectue un `git pull`
puis lance `TeamViewerQS.exe`.

Depuis la version 1.33 de ce depot, `TeamViewerQS.exe` est un programme de
transition qui :

1. telecharge l'installeur `1.33` depuis la release GitHub `v1.33` ;
2. verifie son SHA-256 ;
3. installe Assistance RAIDBOX silencieusement ;
4. configure `latest.json` comme manifeste de mise a jour GitHub ;
5. lance la nouvelle application.

Une fois la version installee lancee, le manifeste `latest.json` met
automatiquement l'application a jour vers la version stable `1.36`.

Depuis la version 1.36, les scripts PowerShell de l'application sont embarques
dans `AssistanceRaidbox.exe`. Le demarrage ne depend donc plus de la politique
d'execution des fichiers `.ps1` de Windows.

Le code source du programme de transition est dans `MigrationTo133.cs`.

## Retour arriere

Etat Git avant la migration :

```text
backup-before-1.33-production-20260612
```
