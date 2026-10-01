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
| `placePopup` | replace un menu flottant dans le canvas, après mesure de sa taille |
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
| Clic droit | menu contextuel (nœud, arête, canvas), repositionné dans le canvas après mesure |
| Clic gauche ailleurs | referme le menu contextuel, sans annuler l'action du clic |
| Clic droit ailleurs | referme l'ancien menu et ouvre le nouveau à l'endroit cliqué |
| `Échap` | referme les menus flottants (contextuel et disposition) |
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

## Arêtes et multigraphe

Une arête porte : `Color`, `Type` (chaîne libre : `flow`, `dépendance`, `retour`…),
`Label`, `Directed` (flèche ou non), `Width`, `Style`, `Dashed`, `Curvature`,
`Visible`, `Highlighted` et un dictionnaire `Data`.

```csharp
var a = graph.AddNode("A");
var b = graph.AddNode("B");

// Deux liaisons entre le même couple : c'est un multigraphe, autorisé.
var forward = graph.AddEdge(a.Id, b.Id);
forward.Type = "dépendance";

var backward = graph.AddEdge(a.Id, b.Id);
backward.Directed = false;          // pas de flèche
backward.Type = "retour";
backward.Color = "#f97316";

graph.ParallelCount(forward);        // 2
graph.ParallelEdges(forward);        // les deux arêtes du couple
```

`Ctrl`+glisser entre deux nœuds déjà reliés crée une liaison supplémentaire au lieu
d'être refusé.

### Rendu des liaisons parallèles

Les arêtes partageant le même couple de nœuds sont groupées (la clé est **non
orientée** : A→B et B→A partagent le même groupe). Sur k arêtes, la courbure vaut
`-(k-1)/2 … +(k-1)/2` fois un pas qui s'élargit avec k : les traits se répartissent
symétriquement de part et d'autre de l'axe et ne se superposent jamais.

```
   ╭────────╮      3 liaisons parallèles : la courbure ne dépend que du rang
───╯        ╰───   (ligne droite au milieu)
```

La courbure propre de l'arête est ignorée au sein d'un groupe, sinon le faisceau
basculerait d'un bord à l'autre. Une liaison d'un groupe est toujours tracée en
courbe, c'est la seule façon de la distinguer ; son style personnel s'applique à
dès qu'elle redevient unique.

Chaque liaison est sélectionnable au clic, puis modifiable (type, couleur, largeur,
style, sens) ou supprimable depuis l'inspecteur ou le menu contextuel.

### Boucles et pseudographe

La terminologie de [wikipedia/Multigraph](https://en.wikipedia.org/wiki/Multigraph)
distingue le **multigraphe** (arêtes parallèles, sans boucle) du **pseudographe**
(même chose, plus les boucles). Le choix se fait par option :

```csharp
graph.AllowSelfLoops = false;   // multigraphe au sens strict (défaut)
graph.AllowSelfLoops = true;    // pseudographe : A -> A devient possible
```

Une boucle se crée en relâchant `Ctrl`+glisser sur le nœud de départ lui-même, via
le menu contextuel (« Ajouter une boucle ») ou le bouton `+` de l'inspecteur.

Côté dessin, une boucle est une goutte refermée sur le nœud : elle part d'un angle
et y revient par l'angle symétrique, le point de contrôle étant placé à
l'extérieur. Les extrémités sont ancrées sur la **frontière réelle de la forme**
(et non sur un cercle), pour qu'une boucle sur un carré ou un losange ne mords pas
dans le nœud. Plusieurs boucles d'un même nœud sont écartées en éventail autour de
la direction opposée au barycentre des voisins.

### Degré

Convention pseudographe : chaque arête parallèle compte séparément et **une boucle
compte deux fois**, puisqu'elle est à la fois entrante et sortante.

```csharp
graph.OutDegree("n1");   // arêtes dont n1 est la source
graph.InDegree("n1");    // arêtes dont n1 est la cible
graph.Degree("n1");      // OutDegree + InDegree → une boucle vaut 2
graph.LoopsOf("n1");     // boucles du nœud
```

Avec 4 boucles, `Degree` vaut donc 8. Le degré est affiché dans l'inspecteur du
nœud (`8 → 4 ← 4`).

Les boucles sont exclues des ressorts des algorithmes de disposition : leur
« ressort » relierait un nœud à lui-même, donc de distance nulle, et produirait
une force infinie.

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
- multigraphe : 4 liaisons entre deux nœuds, courbures −0,24 / −0,08 / +0,08 /
  +0,24 et milieux à Y = −22 / −7 / +7 / +22 (faisceau symétrique, traits distincts)
- sélection ciblée des 5 arêtes du graphe de démonstration, une par une
- bascule orientée / non orientée, type et couleur persistés dans le modèle
- suppression d'une liaison depuis l'inspecteur (5 → 4 arêtes)
- menu contextuel refermé par : clic sur le canvas, clic sur un bouton de
  l'interface, `Échap` — et laissé ouvert si le clic est à l'intérieur
- clic droit ailleurs : l'ancien menu est remplacé par le nouveau (nœud, arête)
- pseudographe : boucle refusée en mode strict, acceptée une fois l'option active
- 4 boucles sur un même nœud : 4 milieux de courbe distincts, degré = 8 (→ 4 ← 4)
- boucles ancrées sur la frontière réelle de la forme (carré, losange, hexagone)
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