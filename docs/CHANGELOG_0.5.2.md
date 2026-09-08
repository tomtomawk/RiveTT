# RiveTT 0.5.2

Correctif de la release automatisée de `v0.5.1`.

## CI sans Revit

- L’enrichissement des réponses du routeur ne référence plus directement
  `Autodesk.Revit.DB.Document`. Les tests de verrou et de navigation sur faux outils
  s’exécutent donc sur GitHub Actions sans installation de Revit, au lieu d’échouer au
  chargement de `RevitAPI`.
- Les tests qui requièrent réellement l’API Revit restent ignorés proprement par les
  attributs `RequiresRevit*` sur un runner sans Revit.
- `batch_export`, devenu une écriture en 0.5.1, publie et transmet désormais
  `dryRun`; le routeur le refuse explicitement puisque l’outil ne propose pas
  d’aperçu.

## Vérification

Suite de tests exécutée avec `REVIT_INSTALL_DIR` pointant vers un dossier sans Revit :
**571 réussis, 20 ignorés, 0 échec**. Le tag `v0.5.2` déclenche la construction et la
publication de l’installateur dans GitHub Actions.

## Signature obligatoire

- `builder\build.ps1` refuse désormais de produire un installateur sans certificat
  Authenticode dont le signataire est exactement `Thomas Thébault`.
- Le désinstalleur et les binaires RiveTT sont signés avec le même certificat.
- Le workflow de release GitHub importe le PFX depuis les secrets
  `RIVETT_SIGN_PFX_BASE64` et `RIVETT_SIGN_PFX_PASSWORD`; leur absence arrête la
  release avant publication.
