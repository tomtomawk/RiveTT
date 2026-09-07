# Campagnes de recette

La recette active est [le protocole en deux phases](../references/protocole-de-recette.md) :
Revit fermé puis création depuis gabarit ; lecture d'une copie de projet déjà ouverte.
Il contient les chemins absolus des entrées et sorties pour l'agent.

L'agent reçoit uniquement le skill et le protocole : il n'a accès au PC que par
Revit/MCP. Le modèle de rapport est intégré au protocole ; le fichier séparé reste
une copie de commodité pour le dépôt et n'est pas à lui transmettre.
Les copies et contrôles disque sont demandés à l'utilisateur. Le rapport est rendu
en Markdown dans la conversation, ou en pièce jointe si le client le permet, puis
enregistré par l'utilisateur.

Copier [le modèle de rapport](modele-rapport.md) vers rapport-`<run>`.md.
Un rapport est rempli pendant l'exécution, jamais prévalidé. Garder les preuves
et modèles dans le dossier de campagne indiqué par le protocole.

Les anciennes recettes v0.4 et leur plan de correctifs ont été retirés de la
documentation active. Les changelogs conservent l'historique ; les régressions
utiles sont reprises dans le protocole courant. Aucun fichier Revit source
n'est supprimé ou considéré comme sain à partir d'un ancien résultat.
