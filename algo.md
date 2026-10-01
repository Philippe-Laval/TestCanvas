# Algorithmes ajoutés
Basées sur les forces — le principe est bien celui que vous décrivez : répulsion entre tous les nœuds, attraction le long des arêtes, itération jusqu'à l'équilibre.

Algorithme	Principe
Force dirigée	Coulomb (k²/d²) + Hooke, refroidissement
Fruchterman–Reingold	k²/d et d²/k, déplacement borné par température décroissante
Spring Embedder (Eades)	Attraction arête par arête, plus rapide sur les graphes denses
Kamada–Kawai	Relaxation du stress : les distances euclidiennes rejoignent celles du graphe
S'y ajoutent Hiérarchique et Radiale (topologie), Circulaire, Grille et Aléatoire. Tous sont calculés en C# et appliqués en une seule notification au canvas.

# Le popup
Le bouton Disposition ▾ ouvre un menu groupé par famille (« Basées sur les forces », « Selon la topologie », « Géométriques »), chaque entrée portant son résumé, plus deux réglages espacement et itérations et un bouton pour relancer. Le catalogue est un vrai registre (LayoutAlgorithms) : ajouter un algorithme = une méthode dans GraphLayout + une entrée dans la liste, le menu se met à jour seul.