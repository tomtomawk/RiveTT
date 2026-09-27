# 01 — Retour d'expérience de la session de test

## 1. Contexte

- Demande : « crée un nouveau projet et dessine un logement sympa, R+2 et R+3 », puis itérations successives.
- Outillage : RiveTT 0.5.4 (≈ 200 outils, chargés à la demande), Revit 2026, gabarit agence, charte de dessin HTML fournie en pièce jointe.
- Niveau de réflexion de l'agent : **faible** (réglage de la session).
- Environ 70 appels d'outils ; 25 à 30 perdus (erreurs, relances, corrections).

## 2. Déroulé et résultat

| Étape | Résultat | Problèmes |
|---|---|---|
| Création du projet | OK depuis le gabarit | Dossier cible inexistant → message clair, nouveau chemin |
| Enveloppe (2 plots + noyau) | OK en 1 script | Toiture zinc : 1er essai en échec (type introuvable par nom exact) |
| Attache murs → toit | OK après 7 appels | Débord de toit pénétrant le noyau → échec du mur ouest |
| Plans v1 | 81 pièces, 81 menuiseries | Sde et entrées trop petites, T4 surdimensionné |
| Plans v2 + hall, vélos, chemins | OK | Mobilier volant (Z doublé), mal placé (points d'insertion), ascenseur en double |
| Escaliers | OK après ~12 appels | Bug `create_stair` (2ᵉ volée au sol) |
| Plans v3 | Entrées ouvertes, ETEL, gaines, circulation 1,30 | Règles de la charte oubliées en v1 et v2 |
| Étiquettes de typologie | 17 posées | — |
| Noue | OK après 3 appels | Lignes interprétées comme un axe au lieu d'un contour |

## 3. Répartition du temps perdu (estimée en nombre d'appels et d'itérations)

| Rang | Cause | Poids | Responsable |
|---|---|---|---|
| 1 | Erreurs de conception (règles de la charte non appliquées, plans refaits 2 fois) | ~35 % | Agent |
| 2 | Renvoi complet de scripts de 150 à 300 lignes après erreur ou rollback | ~20 % | MCP + agent |
| 3 | Comportements non documentés (escalier, attache au toit, points d'insertion, boîte englobante) | ~20 % | MCP / API Revit |
| 4 | Aucun retour visuel : les erreurs n'ont été vues que sur les captures de l'utilisateur | ~15 % | MCP + agent |
| 5 | Verrou d'écriture, lecture de la charte, dry-run | ~10 % | Négligeable |

La connexion au serveur n'a posé aucun problème (réponses rapides et stables).

## 4. Erreurs de l'agent (à ne pas reproduire)

1. **Charte lue une fois puis sortie du contexte.** Entrée ouverte sur séjour, ETEL, gaines palières, largeurs de circulation : tout était dans la charte (§ 2.11 à 2.13), rien n'a été appliqué avant la 3ᵉ version.
2. **Programme inventé.** Aucune question sur les typologies, surfaces, part PMR. Pas de schéma validé avant de modéliser.
3. **Aucun contrôle visuel.** `batch_export` (PNG) existait mais n'a pas été utilisé ; de toute façon l'image ne remonte pas à l'agent (voir 03).
4. **Suppositions non vérifiées sur les familles** : origine supposée au centre, puis boîte englobante supposée fiable, puis Z supposé absolu pour tous les types de familles.
5. **Aucun contrôle des avertissements** (`list_warnings`, `check_model_health` jamais appelés).
6. **Surface « SHAB » calculée en additionnant des pièces** au lieu d'utiliser `manage_area_plans`.
7. **Lecture de la sélection sans examen de la géométrie** : 4 lignes fermées prises pour un axe (noue).

## 5. Niveau de réflexion

| Phase | Réflexion faible | Recommandation |
|---|---|---|
| Exécution mécanique (créer, enregistrer, étiqueter, recaler) | Suffisante, plus rapide | Faible |
| Conception (programme, plans, dimensionnement) | Source principale d'erreurs : cotes calculées de tête, règles non relues, surfaces non contrôlées | Élevée |
| Diagnostic d'erreur API | Tâtonnements (6 essais pour l'attache au toit) | Moyenne |

Conclusion : un réglage unique n'est pas adapté. Séparer conception et exécution (voir 08) permet de mettre l'effort là où il sert.

## 6. Ce qui aurait rendu l'agent plus pertinent

1. Un **programme** en entrée : typologies, surfaces cibles, nombre de logements, part PMR.
2. Des **plans types agence** (T1 à T4) à adapter plutôt qu'à inventer.
3. Une **charte exploitable par machine** : règles chiffrées + points de contrôle (voir 05), plutôt qu'un document de 500 lignes relu une fois.
4. Une **boucle visuelle** systématique (voir 03).
5. Un **catalogue des familles** avec origine, orientation et emprise réelle (voir 06).
6. Un **validateur** qui empêche de livrer un plan non conforme (voir 02 § validate_dwelling).
