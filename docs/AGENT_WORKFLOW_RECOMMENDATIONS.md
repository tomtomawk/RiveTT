# Recommandations pour fiabiliser et accélérer le travail de l'agent (hors périmètre MCP)

Ce document ne porte pas sur des bugs MCPRVTT27 (voir `MCP_AGENT_IMPROVEMENTS.md`) mais sur ce qui, côté consignes, contexte projet ou organisation de la mission, ferait gagner du temps, des tokens et de la fiabilité lors des prochaines missions de modélisation/test autonomes.

## 1. Fournir en amont un "fichier de départ" propre plutôt qu'un fichier de test chargé
Le fichier `Saint-Malo_avenue aristide briand_46_V4.rvt` porte déjà de nombreux éléments de campagnes de tests précédentes (murs, feuilles cassées, nomenclatures de test). Comme MCPRVTT27 ne sait pas créer un document vierge, toute demande de "nouveau projet" part obligatoirement de ce fichier chargé. Pour un exercice de conception isolé (comme ce T2), fournir un fichier gabarit dédié déjà ouvert dans Revit (`.rvt` vierge basé sur le gabarit architectural, sans historique de test) éviterait de livrer un modèle "parasité" et ferait gagner le temps de créer des niveaux tampons pour isoler le nouvel ouvrage.

## 2. Donner à l'avance les noms exacts (ou IDs) des types système récurrents
Murs, sols, garde-corps et cartouches sont des familles système que l'agent ne peut lister qu'indirectement (via une instance existante ou par essais de nom). Une fiche de référence courte ("nomenclature des types" : nom exact du mur extérieur standard, de la cloison standard, du type de sol dalle sur plot, du type de garde-corps balcon, du cartouche projet) supprimerait la quasi-totalité des essais-erreurs constatés dans cette session (recherche de type béton, recherche infructueuse de garde-corps, etc.) et éviterait des dizaines d'appels d'exploration.

## 3. Préciser d'emblée la convention d'altimétrie attendue
Le comportement de `create_wall.locationLine.z` (indifférent) diffère de celui de `create_door`/`create_window.locationPoint.z` (cote absolue obligatoire). Cette incohérence a coûté 9 échecs avant compréhension. Tant que ce n'est pas corrigé côté MCP, une note de convention interne ("toujours passer z = altitude absolue du niveau, jamais 0") glissée dans les instructions de mission ferait gagner plusieurs allers-retours à chaque session.

## 4. Fournir un mini-référentiel des standards attendus (métrage projet)
La demande "respecter un standard neuf type promoteur ou bailleur social" oblige l'agent à faire une recherche web généraliste et à arbitrer seul des surfaces (chambre, WC séparé, hauteur de garde-corps, allèges de fenêtres). Un tableau de référence interne à l'agence (surfaces minimales par type de pièce, hauteurs de garde-corps normalisées, épaisseurs de mur standard RE2020 de l'agence) éviterait la recherche web et garantirait la cohérence avec les projets réels de l'agence plutôt qu'avec une moyenne nationale générique.

## 5. Clarifier à l'avance le niveau de tolérance sur les contournements "visuellement trompeurs"
Face au blocage du garde-corps, l'agent a choisi de ne rien poser plutôt que de modéliser un muret qui aurait pu passer pour un vrai garde-corps dans les nomenclatures. Une consigne explicite du type "en cas de blocage MCP, privilégier un élément absent et documenté plutôt qu'un élément approximatif" (ou l'inverse, si l'agence préfère un rendu visuellement complet même approximatif) éviterait à l'agent d'arbitrer seul un choix qui a un impact sur le rendu final du livrable.

## 6. Réduire le coût en tokens des explorations de types système
Faute d'un outil de listing des types système, chaque recherche de type (mur béton, sol béton, garde-corps) déclenche un appel `get_available_family_types` qui peut renvoyer des dizaines d'entrées non pertinentes (étiquettes, familles chargeables sans rapport). Un index texte, tenu à jour par l'agence, des types système "recommandés pour la modélisation rapide" (10 à 15 lignes) remplacerait ces recherches et réduirait nettement la consommation de tokens sur ce type de mission.

## 7. Prévoir une checklist de fin de mission fournie par l'agence
Pour ce type de mission (modéliser + mettre en page + exporter + documenter), une checklist courte fournie en tête de consigne (ex. "1) nouveau fichier 2) plan 3) mise en page 4) export PDF 5) chapitre MD blocages 6) CR") éviterait à l'agent de devoir lui-même reconstituer l'ordre des livrables à partir d'une phrase longue et dense, et réduirait le risque d'oubli d'un des six livrables demandés.
