# 09 — Points non abordés en session, à traiter

## 1. Sécurité et intégrité du modèle

- **Document actif = projet réel.** En début de session, le document ouvert était un projet de production (LC20_011). Proposer un garde-fou : outil d'écriture refusé si le document n'est pas sur une liste autorisée ou si un paramètre de projet « TEST / PRODUCTION » l'interdit, sauf confirmation.
- **Travail partagé.** Aucun contrôle des sous-projets ni de la synchronisation n'a été fait. Définir : sous-projet cible des créations, interdiction de `synchronize_with_central` sans demande explicite.
- **Suppressions en masse.** Les scripts ont supprimé des centaines d'éléments par filtre (types `CLO_*`, familles `PTE_Porte*`). Ajouter une limite et un résumé de confirmation au-delà de N éléments, ou au moins un journal des ids supprimés.
- **Annulation.** Chaque script est une transaction Revit ; documenter pour l'utilisateur qu'un Ctrl+Z annule un script entier.

## 2. Traçabilité

- **Journal des scripts** : les scripts sont enregistrés en temporaire (`%LOCALAPPDATA%\RiveTT\scripts`, effacés à la fermeture). Les conserver par projet et par session, avec l'intention, le résultat et la liste des ids créés. Utile pour rejouer, auditer et constituer la bibliothèque de fonctions.
- **Marquage des éléments créés par l'agent** : paramètre partagé (ex. `IA_SESSION`, `IA_SCRIPT`) pour filtrer, isoler ou supprimer proprement ce qui a été créé.
- **Télémétrie du serveur** : durée de chaque appel, taille des réponses, taux d'échec par outil. Permettrait de mesurer ce que ce retour d'expérience estime.

## 3. Qualité et tests du MCP

- **Tests de non-régression** sur un modèle de référence : création de mur, porte hébergée à un étage, escalier 2 volées, attache au toit, pose de mobilier à un étage. Ce sont exactement les cas qui ont cassé.
- **Versionnage du contrat** : noter les changements de comportement (valeurs par défaut, unités) dans un journal accessible à l'agent.
- **Compatibilité Revit** : les observations valent pour Revit 2026 ; revalider sur 2027 (attaches de murs, `set_wall_host`).

## 4. Documentation pour l'agent

- **`SKILL.md` trop long pour être relu** : le découper en une carte des outils (1 page), des règles d'écriture (1 page) et des annexes consultées à la demande.
- **Charte en HTML** : la publier aussi en Markdown ou JSON (règles avec identifiants), plus fiable pour la recherche et le validateur.
- **Où vit la charte** : une seule source (bibliothèque Épure ?) ; le fichier joint à une conversation vieillit.
- **Mémoire entre sessions** : ce que l'agent apprend (familles relevées, bugs) doit revenir dans la documentation, pas seulement dans la conversation.

## 5. Conception architecturale non couverte

- **Réglementation incendie** (logements, 2ᵉ ou 3ᵉ famille selon hauteur) : distances aux escaliers, désenfumage de la cage, portes de recoupement — non vérifiées.
- **Acoustique** : superposition des pièces d'eau, séparatifs entre logements et circulations — non vérifiés.
- **Structure** : descente de charges, alignement des refends d'un niveau à l'autre (l'attique décalé ne s'aligne pas sur les étages).
- **Thermique / RE2020** : compacité, orientation des séjours, protections solaires des baies sud — non traités.
- **Réseaux** : superposition des gaines techniques de logement (chutes, VMC) d'un niveau à l'autre — absentes.
- **Stationnement et abords** : places PMR, cheminement accessible depuis la voirie (pente, largeur) — le chemin a été dessiné sans contrôle.
- **Noue** : pas de pente d'écoulement ni d'exutoire, pas de volume calculé par rapport aux besoins de gestion des eaux pluviales du PLUi.

## 6. Coût d'usage

- Une image ≈ 1 500 à 2 000 tokens ; un script de 300 lignes ≈ 6 000 à 8 000 tokens, renvoyé à chaque erreur. Les scripts paramétrés et les outils par lots réduisent davantage le coût que la suppression des captures.
- Le bloc `execution` répété dans chaque réponse représente une part non négligeable du contexte sur une longue session.
