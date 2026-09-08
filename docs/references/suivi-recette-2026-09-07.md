# Suivi des corrections — recette architecture du 7 septembre 2026

Source : rapport-2026-09-07_210600_Revit2027.md, version comprenant le bloc
architecture (134 cas, 43 outils appelés). Le rapport décrit la version installée
au moment du test. Les corrections ci-dessous concernent les sources : aucun
installateur produit, aucune installation ni modification de la maquette en cours.

## Premier lot corrigé dans le code

« Corrigé » ne signifie pas « validé dans Revit ». Les tests automatisés couvrent
les contrats JSON, les calculs indépendants de Revit et certaines garanties du
code source. Ils ne prouvent pas la géométrie ni les transactions en session réelle.

| Défauts | Correction | Vérification à rejouer |
|---|---|---|
| D-07, D-11 | Les paramètres JSON optionnels absents, null ou Undefined ne passent plus au parseur d'objet/tableau. Les valeurs présentes mal formées restent refusées. | Relancer les six outils signalés en omettant leurs paramètres facultatifs, puis avec une valeur invalide. |
| D-09, D-34 | Description de list_system_types clarifiée ; includeLoadable doit être vrai pour les cartouches et meneaux. Le résultat indique combien de types chargeables correspondants ont été exclus et propose cette option. | OST_CurtainWallMullions et cartouches avec les deux valeurs du paramètre. |
| D-10 | Description de filter_elements alignée sur les filtres réellement publiés. | Lire le schéma MCP actualisé. |
| D-13 | activate_view distingue un ID inexistant d'un élément existant qui n'est pas une vue. | Niveau 608, ID absent, plan valide. |
| D-14 | Description du lot line-based complétée avec topLevelId et topOffset et leurs conventions. | Mur contraint au niveau supérieur, relecture des paramètres. |
| D-15 | Les options de pièces non placées et non fermées sont indépendantes. | Pièce R99 placée sans enceinte ; ajouter une fixture non placée et tester les quatre combinaisons. |
| D-16 | Une pièce non fermée retourne son aire mesurée de zéro, cohérente avec le message. | Relecture de R99. |
| D-17 | Volume null lorsque le calcul des volumes est désactivé ; état explicite par pièce et dans le résultat global. | Export avec calcul désactivé, puis activé par l'utilisateur. |
| D-18 | Épaisseur des types de sols, plafonds et toitures lue dans leur structure composée. | Comparer avec get_compound_structure, dont le sol de 260 mm. |
| D-19, D-20 | Surface : `baseLevelId` est un ID ; `baseElevationMm` est une altitude absolue en mm. L'ancien `baseLevel` est refusé sans compatibilité. Résultat enrichi du type, niveau, décalage réellement lu, altitude et pente. IDs ajoutés seulement après validation de transaction. | Toiture sur niveau 512913 à 2890 mm, plafond RDC + 2500 mm, niveau inexistant, ancien nom refusé, entrée invalide et lot mixte. Vérifier la géométrie indépendamment. |
| D-22, D-23, D-24 | Axes : séquences alphabétiques multi-lettres, nombres et zéros initiaux préservés ; styles publiés ; labels JSON texte ou entier acceptés. Entrée non prise en charge refusée sans repli silencieux. | RK/RL/RM, 01/02/03, nombres JSON et étiquettes invalides en aperçu puis réel. |
| D-25 | Renommage d'axe prévisualisé par transaction annulée, avec nom effectivement obtenu. | Vérifier que le nom reste inchangé après aperçu et change après exécution. |
| D-27 | Le compactage conserve execution et les compteurs originaux, notamment pour list_family_types. | Comparer réponses complète et compacte. |
| D-28 | Le lot point-based expose skipped et warnings structurés, y compris quand aucune insertion ne réussit. Le succès du lot reste compatible avec le contrat existant. | Porte hors plage et lot mélangeant une insertion valide et une invalide ; vérifier les compteurs. |
| D-29 | Totaux de portes et fenêtres dédupliqués par ID avant limitation des listes ; occurrences par pièce exposées séparément. | La porte intérieure partagée compte une seule fois globalement, deux occurrences par pièce. |
| D-30 | Dimensions des ouvertures complétées par des mesures structurées avec unité, valeur affichée et valeur interne. Anciennes chaînes conservées pour compatibilité. | Comparer largeur, hauteur, allège et tête avec les paramètres Revit. |
| D-33 | Politique globale d'unités corrigée : les nombres nus de set_element_parameters suivent le stockage interne ; les chaînes unitaires explicites sont recommandées. | Lire la politique ; comparer une chaîne avec unité et un nombre interne sans changer le contrat existant. |

Migration D-20 : tout appel utilisant `baseLevel` est désormais refusé. Utiliser
`baseLevelId` pour un niveau Revit, ou `baseElevationMm` pour une altitude absolue.
L'outil surface ne gagne pas de dryRun : un aperçu demandé reste refusé avant exécution.

## Défauts encore ouverts ou à qualifier

| Défauts | Travail restant |
|---|---|
| D-01 | Fournir la version Revit sans dépendre du document actif. |
| D-02 | Remplacer le faux compteur texte de ping_revit par une information correctement nommée ou un entier réel. |
| D-03 | Ne pas présenter les capacités par défaut comme des observations lorsqu'aucun document n'est ouvert. |
| D-04 | Harmoniser les erreurs avant exécution, notamment l'absence de document, et leur contexte. |
| D-05 | Comparer les classifications outil par outil ; une variation de compteur seule ne prouve pas un contournement du verrou. L'audit statique courant compte 200 wrappers et 197 outils runtime, avec une métrique d'écriture différente du compteur runtime. |
| D-06 | Harmoniser les codes de fichier introuvable. |
| D-08 | Qualifier l'ID de phase 0 dans Revit ; ne pas le déclarer invalide sur la seule observation du rapport. |
| D-12 | Reproduire le problème de cote avant d'attribuer un échec à l'écart flottant. Aucun arrondi arbitraire appliqué aux données géométriques. |
| D-21 | Compléter les alias anglais des paramètres de toiture et leurs suggestions. |
| D-26 | Décrire les garde-corps automatiques dans l'aperçu d'escalier. |
| D-31 | Distinguer un refus de remplacement de famille des autres causes d'échec. |
| D-32 | Résoudre les homonymes de paramètres de niveau de façon cohérente entre français et anglais, selon la catégorie. |

## Vérification du lot

- Tests Release : 586 réussis, 2 ignorés (dépendances interface Revit), aucun échec.
- Build Release Revit 2027 et Revit 2026 : aucun avertissement ni erreur.
- Audit de surface exécuté : 200 wrappers, 197 outils runtime ; inventaire produit régénéré.
- Aucun packaging, déploiement ou test live effectué. Rejouer les cas ci-dessus
  sur une copie de recette après installation ultérieure d'une paire serveur/plugin cohérente.
