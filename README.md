# Graphe interactif — Blazor Server + canvas JavaScript

Application **Blazor Server** (.NET 10) affichant un graphe (nœuds + arêtes) sur un
`<canvas>` JavaScript. Le modèle C# est la source de vérité : toute modification
faite en C# est poussé sur le canvas, et chaque interaction souris est réinjectée
dans le document.

## Démarrer

```bash
dotnet run
```

Puis ouvrir l'URL indiquée (par défaut `https://localhost:7xxx`).

## Architecture

```
Models/
  GraphDocument.cs     document : nœuds, arêtes, sélection, événements de changement
  GraphNode.cs         nœud (position, forme, couleur, verrou, surbrillance)
  GraphEdge.cs         arête (source/cible, style, flèches, libellé)
  GraphChange.cs       incréments envoyés au canvas + instantanés
  GraphHistory.cs      annulation / rétablissement par instantané
  Enums.cs             NodeShape, EdgeStyle, GraphChangeKind
  ObservableObject.cs  base INotifyPropertyChanged

Services/
  GraphLayout.cs       mises en page C# : forces, topologie, géométrie
  LayoutAlgorithms.cs  catalogue des algorithmes (alimente le menu)
  GraphAlgorithms.cs   plus court chemin, graphe accessible, sous-graphe
  GraphSerializer.cs   sérialisation JSON

Components/Graph/
  GraphCanvas.razor    composant Blazor : charge le module JS, relaie les événements
  GraphCanvasBridge.cs [JSInvokable] : callbacks JS -> C#

wwwroot/js/
  graph-canvas.js      moteur de rendu et d'interaction (module ES)
```

## Le canvas JavaScript

`wwwroot/js/graph-canvas.js` est un module ES chargé par `import`. Il ne connaît
pas le modèle métier : il reçoit un instantané (`setGraph`) et des incréments
(`applyChanges`), et émet les interactions vers C# via `DotNetObjectReference`.

Rendu et interactions :

- formes : cercle, carré, rectangle arrondi, losange, hexagone, triangle
- arêtes : droites, courbes, orthogonales, avec flèches, pointillés et libellés
- grille adaptative à deux niveaux, `ResizeObserver` + `devicePixelRatio`
- déplacement de nœud, déplacement groupé des nœuds sélectionnés
- panoramique (clic gauche sur le vide, molette+bouton, espace, Alt)
- zoom à la molette / pincement, centré sur le curseur
- sélection : clic, `Maj`+clic, `Maj`+glisser (rectangle)
- `Ctrl`+glisser depuis un nœud : création d'arête avec aperçu
- clic droit : menu contextuel rendu par Blazor
- double-clic : nœud ou ajout à lemplacement
- surbrillance dorée des résultats de calcul C#

### Feedback de sélection

Le feedback est dessiné **après** le nœud, sinon le remplissage le masque :

| État | Rendu |
| --- | --- |
| Nœud sélectionné | halo diffus + anneau dashed dans la couleur de sélection |
| Nœud primaire | même halo, anneau **plein** : c'est le dernier cliqué |
| Sélection multiple | cadre pointillé englobant + pastille avec le nombre d'éléments |

Le nœud primaire est connu du côté C# également : `GraphDocument.SelectedNodes`
renvoie les nœuds **dans l'ordre de sélection**, le premier étant celui mis en
avant par le canvas et affiché par l'inspecteur.

### API publique du module

| Fonction | Rôle |
| --- | --- |
| `create` / `dispose` | cycle de vie |
| `setGraph` | remplace tout le contenu |
| `applyChanges` | applique un lot d'incréments (`GraphChange`) |
| `setOptions` | options de rendu et d'interaction |
| `setSelection` / `clearSelection` | synchronise la sélection |
| `fitToView`, `zoomBy`, `setZoom`, `resetView`, `focusNode` | gestion de la vue |
| `screenToGraph`, `getCenter` | conversions de coordonnées |
| `getView`, `setView`, `resize` | état de la vue et redimensionnement |
| `toDataUrl`, `downloadPng` | export image |

## Manipuler le graphe depuis C#

Toute la manipulation passe par `GraphDocument`. Il suffit de muter un nœud :
le canvas est mis à jour automatiquement.

```csharp
// Ajouter
var a = graph.AddNode(new GraphNode("a", "Début", -200, 0));
var b = graph.AddNode("Fin", 200, 0);
graph.AddEdge(a.Id, b.Id, "flux");

// Modifier (mise à jour incrémentale immédiate)
a.Shape = NodeShape.Hexagon;
a.Fill = "#10b981";
a.Data["état"] = "actif";
b.MoveTo(300, 120);          // une seule notification de position

// Supprimer (les arêtes incidentes partent avec, sauf removeConnectedEdges: false)
graph.RemoveNode(a.Id);

// Disposition et algorithmes, entièrement en C#
GraphLayout.ForceDirected(graph);
GraphLayout.Hierarchical(graph);

var chemin = GraphAlgorithms.ShortestPath(graph, "n1", "n4");
graph.SetHighlight(chemin!.Select(n => n.id), null);
```

Les extrémités du bouton **Chemin le plus court** sont les deux nœuds sélectionnés
(le nœud primaire en départ, le suivant en arrivée) ; sans sélection multiple, on
retombe sur le premier nœud du document distinct du nœud sélectionné. S'il n'existe
aucun chemin, la surbrillance précédente est effacée.

// Sérialisation
var json = GraphSerializer.Serialize(graph);
var autre = GraphSerializer.Deserialize(json);
```

### API du composant `<GraphCanvas>`

```razor
<GraphCanvas @ref="_canvas" Document="_graph"
             Options="_options"
             NodeMoved="OnNodeMoved"
             SelectionChanged="OnSelectionChanged"
             LinkRequested="OnLinkRequested"
             ContextMenu="OnContextMenu"
             KeyDown="OnKeyDown" />
```

```csharp
await _canvas.FitToViewAsync();
await _canvas.ZoomByAsync(1.2);
await _canvas.FocusNodeAsync("n3", zoom: 1.5);
await _canvas.RefreshSizeAsync();
await _canvas.ReloadAsync();
await _canvas.DownloadPngAsync("graphe.png");
var pos = await _canvas.GetNodeScreenPositionAsync("n3");
```

## Flux de données

**C# vers JS** — `GraphDocument.Changed` porte un `GraphChange` unitaire
(`NodeAdded`, `NodeUpdated`, `NodeRemoved`, `EdgeAdded`, `EdgeUpdated`,
`EdgeRemoved`) ou un `Reset` complet. Le composant s'abonne et appelle
`applyChanges` : un aller-retour par objet modifié. `BeginBatch` / `EndBatch`
regroupent les modifications (utilisés par les mises en page) en un seul envoi.

**JS vers C#** — le canvas appelle les méthodes `[JSInvokable]` de
`GraphCanvasBridge` : `NotifyNodeMoved`, `NotifySelectionChanged`,
`NotifyLinkRequested`, `NotifyNodeDoubleClicked`, `NotifyCanvasDoubleClicked`,
`NotifyContextMenu`, `NotifyKeyDown`. La position remontée est appliquée au
document, qui la rediffuse ensuite.

Pour limiter le trafic réseau, le déplacement d'un nœud n'émet qu'à la fin du
glisser, et le document est replacé dans le même état — pas de boucle.

## Raccourcis et gestes

| Geste | Effet |
| --- | --- |
| Glisser un nœud | déplacer (groupe si plusieurs sélectionnés) |
| Glisser sur le vide | panoramique |
| Molette | zoom au curseur |
| Clic | sélectionner |
| `Maj`+clic | ajouter / retirer de la sélection |
| `Maj`+glisser | sélection rectangulaire |
| `Ctrl`+glisser depuis un nœud | créer une arête |
| Clic droit | menu contextuel (nœud, arête, canvas) |
| Double-clic | renommer un nœud / en ajouter un |
| `Suppr` | supprimer la sélection |
| `F2` | suffixe de libellé |
| `Espace`+glisser | panoramique |

## Page de démonstration

`Components/Pages/Home.razor` : barre d'outils avec popup de choix des algorithmes de
disposition (groupés par famille, avec réglages *espacement* et *itérations*), ajout de
nœuds, chemin le plus court, annulation, inspecteur de l'élément sélectionné,
journal des événements, sérialisation JSON, export PNG, menu contextuel Blazor.

## Algorithmes de disposition

Tous les algorithmes sont calculés en C# et appliqués en une seule notification au
canvas (via `BeginBatch` / `EndBatch`). Ils sont accessibles depuis le popup
**Disposition** de la barre d'outils.

### Basées sur les forces (force-based)

Système de forces appliqué entre les nœuds et les arcs : répulsion entre tous les
nœuds, attraction le long des arêtes, puis itération jusqu'à l'équilibre.

| Algorithme | Principe |
| --- | --- |
| Force dirigée | Répulsion de Coulomb (k²/d²) et ressorts de Hooke, avec refroidissement |
| Fruchterman–Reingold | Forces k²/d et d²/k, déplacement borné par une température décroissante |
| Spring Embedder (Eades) | Attraction traitée arête par arête ; plus rapide sur les graphes denses |
| Kamada–Kawai | Relaxation du stress : les distances euclidiennes rejoignent celles du graphe |

### Selon la topologie

| Algorithme | Principe |
| --- | --- |
| Hiérarchique | Une couche par profondeur depuis les racines |
| Radiale | Un cercle par distance au nœud le plus central |

### Géométriques

| Algorithme | Principe |
| --- | --- |
| Circulaire | Nœuds sur un anneau, ordonnés par parcours pour limiter les croisements |
| Grille | Disposition régulière en lignes et colonnes |
| Aléatoire | Positions tirées au hasard (graine fixe, donc reproductible) |

### Utilisation

```csharp
// Via le catalogue, comme le fait le menu
var descriptor = LayoutAlgorithms.Get(LayoutAlgorithm.FruchtermanReingold);
descriptor.Apply(graph, new LayoutOptions { Spacing = 150, Iterations = 300 });

// Ou directement
GraphLayout.KamadaKawai(graph);
```

`LayoutOptions` porte les paramètres communs : `Spacing` (longueur de repos des
ressorts, ou espacement de base), `Iterations`, `Seed`, `Columns`. Kamada–Kawai
étant en O(n²) par itération, il bascule automatiquement sur l'algorithme à forces
au-delà de 220 nœuds.

Les nœuds verrouillés (`Locked`) ne sont jamais déplacés. Les nœuds à degree nul
peuvent dériver, la répulsion ayant une longue portée : la disposition est donc
bornée à `Spacing × √n × 6`.

## Sélection

```csharp
// Un seul nœud : annule la précédente.
graph.SelectNode("n1");

// Ajout cumulatif (équivalent de Maj+clic).
graph.SelectNode("n2", additive: true);

// Applique une sélection entière venue du canvas, en une notification.
graph.SetSelection(["n3", "n1", "n2"], edgeId: null);

graph.ClearSelection();

// Le nœud mis en avant par le canvas est le premier de la liste.
var actif = graph.PrimarySelectedNode;
var tous  = graph.SelectedNodes;
```

## Vérifications effectuées

- déplacement de nœud → journal « Nœud déplacé » et inspecteur à jour
- clic droit → menu contextuel Blazor, actions opérationnelles
- `Ctrl`+glisser → arête créée (5 → 6 arêtes)
- chemin le plus court calculé en C# → halo doré sur nœuds et arêtes
- chemin le plus court sur A-B-C : identique que l'on sélectionne A puis C ou
  C puis A (3 nœuds, 2 arêtes) — le sens de sélection ne doit pas changer le tracé
- cible isolée → « Aucun chemin » et surbrillance précédente effacée
- sélection unique → repli sur le premier nœud distinct du document
- les 9 algorithmes de disposition du popup appliqués sans erreur : positions
  finies, tous les nœuds distincts, aucune superposition
- étendue des dispositions force-based cohérente (≈ 400–900 px pour 6 nœuds)
- paramètre d'espacement pris en compte (grille : pas = espacement × 1,2)
- 60 nœuds / 30 arêtes : les trois algorithmes à forces en ~210 ms
- sélection multiple : clic puis `Maj`+clic successifs (1 → 2 → 3), retrait par
  `Maj`+clic, rectangle de sélection, « Sélectionner tout »
- cohérence du nœud primaire entre l'anneau plein du canvas et l'inspecteur
- déplacement groupé : les nœuds sélectionnés suivent avec le même incrément
- ajout, suppression, vidage, annulation et rétablissement
- sérialisation puis désérialisation sans perte
- export PNG, zoom, recadrage
- aucune erreur console

Limite connue : les vérifications ont été menées via des événements de pointeur
synthétiques, pas avec une souris physique.