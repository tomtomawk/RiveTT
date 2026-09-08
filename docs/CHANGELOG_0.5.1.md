# RiveTT 0.5.1

Correctif de sécurité et de contrat issu de la recette Revit 2027 du 7 septembre
2026. Aucun installateur n'est produit ni validé par ce commit.

## Corrections

- `create_surface_based_element` et `create_line_based_element` refusent désormais
  la clé ambiguë `baseLevel`. Utiliser `baseLevelId` pour un identifiant de niveau
  Revit ou `baseElevationMm` pour une altitude absolue en millimètres. Cette rupture
  supprime le chemin qui pouvait créer une géométrie à une altitude massive sans
  avertissement (D-20).
- `batch_export` est une écriture : il est désormais bloqué lorsque le verrou
  d'écriture RiveTT est fermé. Cela aligne son comportement sur `export_schedule`
  et protège également les écritures sur disque (D-58).

## Documentation

Le `SKILL.md` installé, le suivi de recette et la description MCP des outils
concernés reflètent les nouveaux contrats. L'inventaire de surface doit être
régénéré avant publication.

## Vérification à effectuer

- Rejouer D-20 dans Revit 2026.5 et 2027 : `baseLevel` doit être refusé avant
  transaction ; `baseLevelId` et `baseElevationMm` doivent créer à l'altitude
  demandée.
- Rejouer D-58 verrou fermé puis ouvert : `batch_export` ne doit créer aucun fichier
  verrou fermé et doit exporter normalement verrou ouvert.
- Compiler, lancer la suite de tests et produire l'installateur avant diffusion.
