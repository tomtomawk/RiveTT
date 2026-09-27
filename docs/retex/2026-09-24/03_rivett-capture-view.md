# 03 — Spécification : retour d'image Revit → MCP → agent

## 1. État actuel (test du 24/09/2026)

| Étape | Résultat |
|---|---|
| `batch_export` format IMAGE, vue `3D_Présentation` | ✅ PNG écrit sur `C:\Users\…\Temp\rivett_test\3D_Présentation.png` |
| Contenu renvoyé à l'agent | Texte seulement : nom de fichier, `success: true` |
| Lecture du fichier via `send_code_to_revit` | ❌ Refusé : `System.IO`, `File.ReadAllBytes` interdits (règle à conserver) |
| Lecture depuis l'environnement de l'agent | ❌ Impossible : l'agent travaille dans un conteneur distant, sans accès au disque local |

**Conclusion** : Revit sait produire l'image ; la chaîne s'arrête au disque local. Le maillon manquant est la lecture du fichier par le plugin et son renvoi en contenu MCP.

## 2. Architecture cible

```
Agent ──appel capture_view──► Serveur MCP local ──► Plugin RiveTT (ExternalEvent)
                                                         │ doc.ExportImage(...)
                                                         ▼
                                                   PNG temporaire (disque local)
                                                         │ File.ReadAllBytes (autorisé côté plugin)
                                                         ▼ base64 + métadonnées ; suppression du fichier
Agent ◄── content: [ image/png , texte JSON ] ◄── Serveur MCP local
```

L'interdiction d'E/S ne concerne que le code envoyé par l'agent via `send_code`. Le plugin, lui, peut lire le fichier qu'il vient d'écrire.

## 3. Format de réponse MCP

```json
{
  "content": [
    { "type": "image", "data": "<base64>", "mimeType": "image/png" },
    { "type": "text", "text": "{\"view\":\"R+1\",\"viewId\":512930,\"pixelSize\":[1600,1043],\"modelBboxMm\":[-1000,-4000,37000,14000],\"mmPerPixel\":23.75,\"origin\":\"top-left = (xmin, ymax)\",\"scale\":100}" }
  ]
}
```

Le bloc texte est indispensable : il permet à l'agent de convertir une position en pixels en coordonnées projet (« le lit à (412, 300) px → x = 16,2 m, y = 9,4 m ») et de corriger sans deviner.

## 4. Signature proposée

| Paramètre | Type | Défaut | Rôle |
|---|---|---|---|
| `viewId` / `viewName` | id / texte | vue active | Vue à capturer (plan, 3D, coupe, élévation) |
| `bboxMm` | [xmin, ymin, xmax, ymax] | vue entière | Zone à cadrer (plans) |
| `pixelSize` | entier | 1600 | Grand côté, 800 à 2000 |
| `detailLevel` | Coarse/Medium/Fine | vue | Niveau de détail temporaire |
| `displayStyle` | HLR/Shading/ShadingWithEdges/Realistic | vue | Rendu temporaire |
| `highlightIds` | liste d'ids | — | Surlignage rouge (surcharge graphique temporaire) |
| `isolateCategories` | liste | — | Masquer le reste (ex. meubles seuls) |
| `showIds` | catégorie | — | Étiquettes temporaires avec l'id des éléments |
| `returnMode` | `inline` / `url` | `inline` | Repli si le connecteur ne relaie pas les images |

## 5. Implémentation côté plugin (C#)

```csharp
// Dans l'ExternalEvent. Travailler sur une vue temporaire pour ne rien modifier.
using (var tg = new TransactionGroup(doc, "capture")) {
  tg.Start();
  View v = source;
  using (var t = new Transaction(doc, "tmp")) {
    t.Start();
    if (needsTemp) {
      var id = source.Duplicate(ViewDuplicateOption.WithDetailing);
      v = (View)doc.GetElement(id);
      // cadrage, niveau de détail, style, surcharges, masquages…
    }
    t.Commit();
  }
  var dir = Path.Combine(Path.GetTempPath(), "rivett_cap_" + Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(dir);
  var opt = new ImageExportOptions {
    ExportRange = ExportRange.SetOfViews,
    FilePath = Path.Combine(dir, "cap"),
    PixelSize = pixelSize,
    FitDirection = FitDirectionType.Horizontal,
    ImageResolution = ImageResolution.DPI_150,
    HLRandWFViewsFileType = ImageFileType.PNG,
    ShadowViewsFileType = ImageFileType.PNG
  };
  opt.SetViewsAndSheets(new List<ElementId> { v.Id });
  doc.ExportImage(opt);
  var file = Directory.GetFiles(dir, "*.png").Single();   // Revit suffixe le nom de la vue
  var b64 = Convert.ToBase64String(File.ReadAllBytes(file));
  Directory.Delete(dir, true);
  tg.RollBack();                                          // supprime la vue temporaire
  return new { imageBase64 = b64, mime = "image/png", /* métadonnées */ };
}
```

Points d'attention :
- **Nom du fichier** : Revit ajoute le type et le nom de la vue ; utiliser un dossier dédié et chercher le PNG par motif.
- **Correspondance pixels ↔ mm** : lire le `CropBox` de la vue (plans) et la taille réelle de l'image ; pour la 3D, fournir seulement orientation et boîte de coupe.
- **Vues 3D en rendu réaliste** : vérifier le comportement sur une vue non active.
- **`TransactionGroup.RollBack()`** : garantit que la capture ne laisse aucune trace dans le modèle.
- **Taille** : 1 600 px de grand côté suffisent. Au-delà, l'image est réduite côté modèle et coûte plus (≈ 1 500 à 2 000 tokens par image).

## 6. Plan de test

1. **Relais du connecteur, sans Revit** : outil de test qui renvoie un PNG fixe (carré rouge + texte « TEST 42 »). L'agent doit lire « TEST 42 ». Sinon le problème vient du connecteur, pas du serveur.
2. **Export d'une vue existante** : plan RDC, `pixelSize` 1600.
3. **Cadrage** par `bboxMm` sur un logement.
4. **Surlignage** d'une liste d'ids.
5. **Vérification** qu'aucune vue temporaire ne reste dans le modèle.

## 7. Repli si le connecteur ne relaie pas les images

Publier le PNG sur un stockage (R2 Cloudflare) avec une URL signée à durée courte, et renvoyer l'URL dans le texte : l'agent peut l'ouvrir. Prévoir l'effacement automatique.

## 8. Usage attendu par l'agent

Contrôle obligatoire après chaque étape d'écriture importante : un plan par niveau modifié + une vue 3D. Sur la session de test, cela aurait révélé avant l'utilisateur : mobilier volant, balcons manquants, ascenseur en double, entrées fermées.
