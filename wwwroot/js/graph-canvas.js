/**
 * graph-canvas.js
 * ---------------------------------------------------------------------------
 * Moteur de rendu et d'interaction pour un canvas de graphe piloté par Blazor.
 *
 * Le module ne connaît pas le modèle métier : il reçoit un instantané
 * (nœuds + arêtes) et un jeu d'options, puis remonte les interactions
 * utilisateur via DotNetObjectReference. C#, reste maître du document.
 * ---------------------------------------------------------------------------
 */

const instances = new WeakMap();

let instanceCounter = 0;

/* ------------------------------------------------------------------ utilitaires */

const clamp = (value, min, max) => (value < min ? min : value > max ? max : value);

/** Distance entre un point et un segment. */
function distanceToSegment(px, py, ax, ay, bx, by) {
    const dx = bx - ax;
    const dy = by - ay;
    const lengthSq = dx * dx + dy * dy;

    if (lengthSq === 0) {
        return Math.hypot(px - ax, py - ay);
    }

    let t = ((px - ax) * dx + (py - ay) * dy) / lengthSq;
    t = clamp(t, 0, 1);

    return Math.hypot(px - (ax + t * dx), py - (ay + t * dy));
}

/**
 * Échantillonne une géométrie en polyline : nécessaire pour tester la distance
 * à une courbe, l'approximation par les seuls points de contrôle étant trop
 * grossière (cas des boucles).
 */
function sampleGeometry(geometry, segments = 12) {
    if (geometry.type === 'line') {
        return geometry.points.slice(0, 2);
    }

    if (geometry.type === 'curve') {
        const [p0, ctrl, p1] = geometry.points;
        const samples = [];

        for (let i = 0; i <= segments; i++) {
            const t = i / segments;
            const inv = 1 - t;
            samples.push({
                x: inv * inv * p0.x + 2 * inv * t * ctrl.x + t * t * p1.x,
                y: inv * inv * p0.y + 2 * inv * t * ctrl.y + t * t * p1.y
            });
        }

        return samples;
    }

    return geometry.points;
}

/* ------------------------------------------------------------------- GraphCanvas */

class GraphCanvas {
    constructor(canvas, dotNetRef, options = {}) {
        this.canvas = canvas;
        this.ctx = canvas.getContext('2d');
        this.dotNetRef = dotNetRef;
        this.id = ++instanceCounter;

        this.options = Object.assign({
            showGrid: true,
            gridSize: 24,
            snapToGrid: false,
            minZoom: 0.15,
            maxZoom: 5,
            panSpeed: 1,
            nodeBorderWidth: 2,
            parallelEdgeSpread: 0.16,
            allowSelfLoops: false,
            popupMargin: 200,
            selectionColor: '#38bdf8',
            fontFamily: "'Segoe UI', system-ui, -apple-system, sans-serif",
            theme: {
                background: '#0f172a',
                grid: 'rgba(148, 163, 184, 0.10)',
                gridStrong: 'rgba(148, 163, 184, 0.22)',
                edge: '#94a3b8'
            }
        }, options);

        if (options.theme) {
            this.options.theme = Object.assign({
                background: '#0f172a',
                grid: 'rgba(148, 163, 184, 0.10)',
                gridStrong: 'rgba(148, 163, 184, 0.22)',
                edge: '#94a3b8'
            }, options.theme);
        }

        this.nodes = new Map();
        this.edges = new Map();

        // Arêtes partageant le même couple de nœuds (multigraphe), recalculé à
        // chaque modification du document. Les boucles sont indexées à part,
        // par nœud.
        this.pairGroups = new Map();
        this.loopGroups = new Map();

        this.view = { x: 0, y: 0, zoom: 1 };
        this.dpr = window.devicePixelRatio || 1;
        this.width = 0;
        this.height = 0;

        this.mode = 'idle';           // idle | dragNode | pan | marquee | link
        this.activeNodeId = null;
        this.hoverNodeId = null;
        this.hoverEdgeId = null;
        this.spacePressed = false;
        this.selection = new Set();  // ids de nœuds sélectionnés
        this.primaryNodeId = null;  // dernier nœud cliqué, au sein de la sélection
        this.activeEdgeId = null;

        this.dragOffset = { x: 0, y: 0 };
        this.panStart = null;
        this.marqueeStart = null;
        this.marqueeRect = null;
        this.linkStartId = null;
        this.linkCurrent = null;
        this.hasFocus = false;
        this.disposed = false;

        this._needsRender = true;
        this._frame = null;

        this._onPointerDown = this.handlePointerDown.bind(this);
        this._onPointerMove = this.handlePointerMove.bind(this);
        this._onPointerUp = this.handlePointerUp.bind(this);
        this._onWheel = this.handleWheel.bind(this);
        this._onDblClick = this.handleDoubleClick.bind(this);
        this._onContextMenu = this.handleContextMenu.bind(this);
        this._onKeyDown = this.handleKeyDown.bind(this);
        this._onKeyUp = this.handleKeyUp.bind(this);
        this._onFocus = () => { this.hasFocus = true; };
        this._onBlur = () => { this.hasFocus = false; };

        this.attach();
        this.resize();

        // ResizeObserver : le canvas peut être redimensionné sans que la fenêtre
        // change (panneau redimensionnable, bascule de mise en page, zoom navigateur…).
        if (typeof ResizeObserver !== 'undefined') {
            this._observer = new ResizeObserver(() => this.resize());
            this._observer.observe(this.canvas);
        }

        this._onResize = this.resize.bind(this);
        window.addEventListener('resize', this._onResize);

        this._loop = this.loop.bind(this);
        this._frame = requestAnimationFrame(this._loop);
    }

    /* -------------------------------------------------------------- cycle de vie */

    attach() {
        const c = this.canvas;
        c.style.touchAction = 'none';
        c.addEventListener('pointerdown', this._onPointerDown);
        c.addEventListener('pointermove', this._onPointerMove);
        c.addEventListener('pointerup', this._onPointerUp);
        c.addEventListener('pointercancel', this._onPointerUp);
        c.addEventListener('wheel', this._onWheel, { passive: false });
        c.addEventListener('dblclick', this._onDblClick);
        c.addEventListener('contextmenu', this._onContextMenu);
        c.addEventListener('focus', this._onFocus);
        c.addEventListener('blur', this._onBlur);
        window.addEventListener('keydown', this._onKeyDown);
        window.addEventListener('keyup', this._onKeyUp);
    }

    dispose() {
        if (this.disposed) {
            return;
        }

        this.disposed = true;
        const c = this.canvas;

        c.removeEventListener('pointerdown', this._onPointerDown);
        c.removeEventListener('pointermove', this._onPointerMove);
        c.removeEventListener('pointerup', this._onPointerUp);
        c.removeEventListener('pointercancel', this._onPointerUp);
        c.removeEventListener('wheel', this._onWheel);
        c.removeEventListener('dblclick', this._onDblClick);
        c.removeEventListener('contextmenu', this._onContextMenu);
        c.removeEventListener('focus', this._onFocus);
        c.removeEventListener('blur', this._onBlur);
        window.removeEventListener('keydown', this._onKeyDown);
        window.removeEventListener('keyup', this._onKeyUp);
        window.removeEventListener('resize', this._onResize);

        if (this._observer) {
            this._observer.disconnect();
            this._observer = null;
        }

        if (this._frame) {
            cancelAnimationFrame(this._frame);
        }

        this.nodes.clear();
        this.edges.clear();
    }

    resize() {
        const rect = this.canvas.getBoundingClientRect();
        this.dpr = window.devicePixelRatio || 1;

        // Le canvas est dimensionné par le conteneur ; on ne fait qu'ajuster le buffer.
        const width = Math.max(1, Math.floor(rect.width));
        const height = Math.max(1, Math.floor(rect.height));
        const bufferWidth = Math.floor(width * this.dpr);
        const bufferHeight = Math.floor(height * this.dpr);

        if (this.canvas.width !== bufferWidth || this.canvas.height !== bufferHeight) {
            this.canvas.width = bufferWidth;
            this.canvas.height = bufferHeight;
        }

        this.width = width;
        this.height = height;
        this.invalidate();
    }

    invalidate() {
        this._needsRender = true;
    }

    loop() {
        if (this.disposed) {
            return;
        }

        if (this._needsRender) {
            this._needsRender = false;
            this.render();
        }

        this._frame = requestAnimationFrame(this._loop);
    }

    /* -------------------------------------------------------------- conversions */

    /** Écran (pixels CSS) vers repère du graphe. */
    toGraph(clientX, clientY) {
        const rect = this.canvas.getBoundingClientRect();
        return {
            x: (clientX - rect.left - this.view.x) / this.view.zoom,
            y: (clientY - rect.top - this.view.y) / this.view.zoom
        };
    }

    toScreen(x, y) {
        return { x: x * this.view.zoom + this.view.x, y: y * this.view.zoom + this.view.y };
    }

    /* ------------------------------------------------------------------- données */

    setGraph(payload) {
        this.nodes.clear();
        this.edges.clear();
        this.selection.clear();

        const nodes = (payload && payload.nodes) || [];
        for (const node of nodes) {
            this.nodes.set(node.id, this.normalizeNode(node));
        }

        const edges = (payload && payload.edges) || [];
        for (const edge of edges) {
            this.edges.set(edge.id, this.normalizeEdge(edge));
            if (edge.selected) {
                this.activeEdgeId = edge.id;
            }
        }

        for (const node of nodes) {
            if (node.selected) {
                this.selection.add(node.id);

                if (!this.primaryNodeId) {
                    this.primaryNodeId = node.id;
                }
            }
        }

        this.rebuildPairGroups();
    }

    normalizeNode(node) {
        return {
            id: String(node.id),
            label: node.label == null ? '' : String(node.label),
            x: Number(node.x) || 0,
            y: Number(node.y) || 0,
            radius: Number(node.radius) || 28,
            fill: node.fill || '#3b82f6',
            stroke: node.stroke || '#1d4ed8',
            textColor: node.textColor || '#ffffff',
            fontSize: Number(node.fontSize) || 13,
            shape: String(node.shape || 'Circle'),
            locked: !!node.locked,
            visible: node.visible !== false,
            selected: !!node.selected,
            highlighted: !!node.highlighted
        };
    }

    normalizeEdge(edge) {
        return {
            id: String(edge.id),
            sourceId: String(edge.sourceId),
            targetId: String(edge.targetId),
            label: edge.label == null ? '' : String(edge.label),
            type: edge.type == null ? '' : String(edge.type),
            color: edge.color || '#94a3b8',
            width: Number(edge.width) || 2,
            dashed: !!edge.dashed,
            directed: edge.directed !== false,
            selected: !!edge.selected,
            visible: edge.visible !== false,
            curvature: Number(edge.curvature) || 0.25,
            style: String(edge.style || 'Straight'),
            highlighted: !!edge.highlighted
        };
    }

    /** Applique un changement unitaire (ou une liste) envoyé par C#. */
    applyChange(change) {
        switch (change.kind) {
            case 'Reset':
                this.setGraph(change.snapshot);
                return;
            case 'NodeAdded':
                this.nodes.set(change.id, this.normalizeNode(change.node));
                if (change.node.selected) {
                    this.selection.add(change.id);
                }
                break;
            case 'NodeUpdated': {
                const existing = this.nodes.get(change.id);
                if (existing) {
                    const merged = this.normalizeNode(change.node);
                    this.nodes.set(change.id, merged);

                    if (merged.selected) {
                        this.selection.add(merged.id);

                        // Sélection pilotée depuis C# : le nœud promu devient primaire
                        // s'il s'agit du premier de la sélection.
                        if (this.selection.size === 1) {
                            this.primaryNodeId = merged.id;
                        }
                    } else {
                        this.selection.delete(merged.id);

                        if (this.primaryNodeId === merged.id) {
                            this.primaryNodeId = this.selection.values().next().value ?? null;
                        }
                    }
                }
                break;
            }
            case 'NodeRemoved':
                this.nodes.delete(change.id);
                this.selection.delete(change.id);
                break;
            case 'EdgeAdded':
                this.edges.set(change.id, this.normalizeEdge(change.edge));
                if (change.edge.selected) {
                    this.activeEdgeId = change.id;
                }
                break;
            case 'EdgeUpdated':
                if (this.edges.has(change.id)) {
                    const merged = this.normalizeEdge(change.edge);
                    this.edges.set(change.id, merged);
                    this.activeEdgeId = merged.selected ? merged.id : (this.activeEdgeId === merged.id ? null : this.activeEdgeId);
                }
                break;
            case 'EdgeRemoved':
                this.edges.delete(change.id);
                if (this.activeEdgeId === change.id) {
                    this.activeEdgeId = null;
                }
                break;
            default:
                break;
        }

        // Le groupement des arêtes parallèles dépend de la topologie : il doit
        // être refait dès qu'une arête apparaît ou disparaît.
        this.rebuildPairGroups();
    }

    applyChanges(changes) {
        if (!changes) {
            return;
        }

        for (const change of changes) {
            this.applyChange(change);
        }
    }

    setOptions(options) {
        if (!options) {
            return;
        }

        Object.assign(this.options, options);
        if (options.theme) {
            Object.assign(this.options.theme, options.theme);
        }

        if (options.gridSize) {
            this.options.gridSize = Number(options.gridSize) || 24;
        }

        this.invalidate();
    }

    /* ----------------------------------------------------------------- sélection */

    setSelection(nodeIds, edgeId) {
        this.selection = new Set(nodeIds || []);

        // Si le nœud primaire actuel n'est plus sélectionné, on prend le premier.
        if (!this.selection.has(this.primaryNodeId)) {
            this.primaryNodeId = this.selection.values().next().value ?? null;
        }

        this.activeEdgeId = edgeId || null;
        this.invalidate();
    }

    clearSelection() {
        this.selection.clear();
        this.primaryNodeId = null;
        this.activeEdgeId = null;
        this.invalidate();
    }

    /* -------------------------------------------------------------------- vue */

    zoomAt(clientX, clientY, factor) {
        const rect = this.canvas.getBoundingClientRect();
        const sx = clientX - rect.left;
        const sy = clientY - rect.top;
        const before = this.toGraph(clientX, clientY);

        const next = clamp(this.view.zoom * factor, this.options.minZoom, this.options.maxZoom);
        if (next === this.view.zoom) {
            return;
        }

        this.view.zoom = next;
        this.view.x = sx - before.x * this.view.zoom;
        this.view.y = sy - before.y * this.view.zoom;
        this.invalidate();
    }

    zoomBy(factor) {
        const rect = this.canvas.getBoundingClientRect();
        this.zoomAt(
            rect.left + this.width / 2,
            rect.top + this.height / 2,
            factor
        );
    }

    setZoom(zoom, centerX, centerY) {
        const rect = this.canvas.getBoundingClientRect();
        const cx = centerX == null ? rect.left + this.width / 2 : centerX;
        const cy = centerY == null ? rect.top + this.height / 2 : centerY;
        this.zoomAt(cx, cy, clamp(Number(zoom) || 1, this.options.minZoom, this.options.maxZoom) / this.view.zoom);
    }

    getView() {
        return { x: this.view.x, y: this.view.y, zoom: this.view.zoom };
    }

    setView(view) {
        if (!view) {
            return;
        }

        this.view.x = Number(view.x) || 0;
        this.view.y = Number(view.y) || 0;
        this.view.zoom = clamp(Number(view.zoom) || 1, this.options.minZoom, this.options.maxZoom);
        this.invalidate();
    }

    resetView() {
        this.view = { x: 0, y: 0, zoom: 1 };
        this.invalidate();
    }

    /** Cadre l'ensemble des nœuds visibles. */
    fitToView(padding = 60) {
        const nodes = [...this.nodes.values()].filter(n => n.visible);
        if (nodes.length === 0) {
            this.resetView();
            return;
        }

        let minX = Infinity;
        let minY = Infinity;
        let maxX = -Infinity;
        let maxY = -Infinity;

        for (const node of nodes) {
            const r = node.radius + padding;
            minX = Math.min(minX, node.x - r);
            minY = Math.min(minY, node.y - r);
            maxX = Math.max(maxX, node.x + r);
            maxY = Math.max(maxY, node.y + r);
        }

        const graphWidth = Math.max(maxX - minX, 1);
        const graphHeight = Math.max(maxY - minY, 1);
        const zoom = clamp(
            Math.min(this.width / graphWidth, this.height / graphHeight),
            this.options.minZoom,
            this.options.maxZoom
        );

        this.view.zoom = zoom;
        this.view.x = this.width / 2 - ((minX + maxX) / 2) * zoom;
        this.view.y = this.height / 2 - ((minY + maxY) / 2) * zoom;
        this.invalidate();
    }

    focusNode(id, zoom) {
        const node = this.nodes.get(id);
        if (!node) {
            return;
        }

        if (zoom) {
            this.view.zoom = clamp(Number(zoom), this.options.minZoom, this.options.maxZoom);
        }

        this.view.x = this.width / 2 - node.x * this.view.zoom;
        this.view.y = this.height / 2 - node.y * this.view.zoom;
        this.invalidate();
    }

    /* ------------------------------------------------------------- interactions */

    hitTestNode(gx, gy) {
        // Les nœuds sont testés du plus petit au plus grand pour que les petits
        // nœuds posés sur un gros restent sélectionnables.
        const candidates = [...this.nodes.values()]
            .filter(n => n.visible)
            .sort((a, b) => b.radius - a.radius);

        for (const node of candidates) {
            if (this.isPointInNode(node, gx, gy)) {
                return node;
            }
        }

        return null;
    }

    isPointInNode(node, gx, gy) {
        const dx = gx - node.x;
        const dy = gy - node.y;
        const r = node.radius;

        switch (node.shape) {
            case 'Square':
                return Math.abs(dx) <= r && Math.abs(dy) <= r;
            case 'RoundedRectangle': {
                const w = r * 1.25;
                const h = r * 0.8;
                if (Math.abs(dx) > w || Math.abs(dy) > h) {
                    return false;
                }

                const corner = r * 0.35;
                const ix = Math.abs(dx) - (w - corner);
                const iy = Math.abs(dy) - (h - corner);
                if (ix <= 0 || iy <= 0) {
                    return true;
                }

                return ix * ix + iy * iy <= corner * corner;
            }
            case 'Diamond':
                return Math.abs(dx) + Math.abs(dy) <= r;
            case 'Triangle':
                return dy >= -r && dy <= r && Math.abs(dx) <= r * (1 - Math.abs(dy) / (2 * r)) * 2 - (Math.abs(dx) <= r ? 0 : 1e9);
            case 'Hexagon':
                return Math.abs(dx) <= r && Math.abs(dy) <= r * 0.866
                    && Math.abs(dx) * 0.5 + Math.abs(dy) * 0.866 <= r;
            case 'Circle':
            default:
                return dx * dx + dy * dy <= r * r;
        }
    }

    edgeGeometry(edge) {
        const source = this.nodes.get(edge.sourceId);
        const target = this.nodes.get(edge.targetId);

        if (!source || !target) {
            return null;
        }

        const group = this.pairGroups.get(this.pairKey(edge));

        // Boucle : arête d'un nœud à lui-même, dessinée en goutte à l'extérieur.
        if (edge.sourceId === edge.targetId) {
            return this.computeLoop(edge);
        }

        // Multigraphe : la courbure dépend du seul rang dans le faisceau. La
        // courbure propre de l'arête est ignorée ici, sinon le faisceau se
        // décalerait d'un bord à l'autre au lieu de rester symétrique.
        if (group && group.length > 1) {
            return this.computePath(source, target, 'Curved', this.parallelOffset(edge));
        }

        return this.computePath(source, target, edge.style, edge.curvature);
    }

    /**
     * Géométrie d'une boucle : l'arête quitte le nœud par un angle, en revient
     * par l'angle symétrique, en formant une goutte à l'extérieur.
     */
    computeLoop(edge) {
        const node = this.nodes.get(edge.sourceId);

        if (!node) {
            return null;
        }

        const loops = this.loopGroups.get(edge.sourceId) || [];
        const index = Math.max(0, loops.indexOf(edge.id));
        const theta = this.loopAngle(node, index, loops.length);
        const r = node.radius;
        const half = Math.min(0.8, 0.35 + 0.12 * loops.length);
        const bulge = r * (1.15 + 0.35 * index);

        // Les ancrages suivent la frontière réelle de la forme : sur un carré
        // ou un losange, un rayon circulaire ferait mordre la boucle dans le nœud.
        const r1 = this.boundaryDistance(node, theta - half);
        const r2 = this.boundaryDistance(node, theta + half);

        const p1 = { x: node.x + r1 * Math.cos(theta - half), y: node.y + r1 * Math.sin(theta - half) };
        const p2 = { x: node.x + r2 * Math.cos(theta + half), y: node.y + r2 * Math.sin(theta + half) };
        const ctrl = {
            x: node.x + (r + bulge) * Math.cos(theta),
            y: node.y + (r + bulge) * Math.sin(theta)
        };

        return {
            type: 'curve',
            loop: true,
            points: [p1, ctrl, p2],
            label: {
                x: 0.25 * p1.x + 0.5 * ctrl.x + 0.25 * p2.x,
                y: 0.25 * p1.y + 0.5 * ctrl.y + 0.25 * p2.y
            },
            // Tangente en fin de courbe : c'est elle qui oriente la flèche.
            angle: Math.atan2(p2.y - ctrl.y, p2.x - ctrl.x)
        };
    }

    /**
     * Distance du centre à la frontière de la forme dans une direction donnée.
     * Inverse de <see cref="isPointInNode"/> pour les formes polygonales.
     */
    boundaryDistance(node, angle) {
        const r = node.radius;
        const cos = Math.abs(Math.cos(angle));
        const sin = Math.abs(Math.sin(angle));

        switch (node.shape) {
            case 'Square':
                return r / Math.max(cos, sin, 1e-6);
            case 'RoundedRectangle': {
                const w = r * 1.25;
                const h = r * 0.8;
                return Math.min(w / Math.max(cos, 1e-6), h / Math.max(sin, 1e-6));
            }
            case 'Diamond':
                return r / Math.max(cos + sin, 1e-6);
            case 'Hexagon':
                // Dodécagone approché : apothème r·cos(30°) sur une face.
                return (r * Math.cos(Math.PI / 6))
                    / Math.max(cos * Math.cos(Math.PI / 6) + sin * Math.sin(Math.PI / 6), 1e-6);
            case 'Triangle':
                return r / Math.max(cos + sin * 0.5, 1e-6);
            case 'Circle':
            default:
                return r;
        }
    }

    /**
     * Angle d'une boucle. La direction de base pointe à l'opposé du barycentre
     * des voisins, pour que la boucle reste dans l'espace libre ; les boucles
     * suivantes d'un même nœud sont écartées en éventail.
     */
    loopAngle(node, index, count) {
        let base = -Math.PI / 2;
        let sumX = 0;
        let sumY = 0;

        for (const edge of this.edges.values()) {
            let other = null;

            if (edge.sourceId === node.id && edge.targetId !== node.id) {
                other = this.nodes.get(edge.targetId);
            } else if (edge.targetId === node.id && edge.sourceId !== node.id) {
                other = this.nodes.get(edge.sourceId);
            }

            if (other) {
                sumX += other.x;
                sumY += other.y;
            }
        }

        if (sumX !== 0 || sumY !== 0) {
            base = Math.atan2(node.y - sumY, node.x - sumX);
        }

        return base + (index - (count - 1) / 2) * 0.85;
    }

    /**
     * Clé d'un couple de nœuds, indépendante du sens : A→B et B→A partagent le
     * même couple. C'est ce qui permet de décaler les arêtes parallèles d'un
     * multigraphe.
     */
    pairKey(edge) {
        return edge.sourceId < edge.targetId
            ? `${edge.sourceId}|${edge.targetId}`
            : `${edge.targetId}|${edge.sourceId}`;
    }

    /**
     * Écart de courbure à appliquer à une arête lorsqu'elle partage ses deux
     * extrémités avec d'autres. Sur k arêtes parallèles, les écarts sont répartis
     * symétriquement autour de zéro : (-(k-1)/2 … +(k-1)/2), ce qui écarte les
     * traits sans jamais les superposer.
     */
    parallelOffset(edge) {
        const group = this.pairGroups.get(this.pairKey(edge));

        if (!group || group.length < 2) {
            return 0;
        }

        const index = Math.max(0, group.indexOf(edge.id));
        const centered = index - (group.length - 1) / 2;

        // Sur k arêtes, les écarts valent -(k-1)/2 … +(k-1)/2 : les traits se
        // répartissent de part et d'autre de l'axe, symétriquement. Le pas
        // s'élargit avec k pour que l'écart reste lisible.
        const step = this.options.parallelEdgeSpread * (1 + 0.25 * (group.length - 2));

        return centered * step;
    }

    /** Recalcule le groupement des arêtes parallèles (invalidé à chaque changement). */
    rebuildPairGroups() {
        const groups = new Map();
        const loops = new Map();

        for (const edge of this.edges.values()) {
            if (edge.sourceId === edge.targetId) {
                if (!loops.has(edge.sourceId)) {
                    loops.set(edge.sourceId, []);
                }

                loops.get(edge.sourceId).push(edge.id);
                continue;
            }

            const key = this.pairKey(edge);
            if (!groups.has(key)) {
                groups.set(key, []);
            }

            groups.get(key).push(edge.id);
        }

        this.pairGroups = groups;
        this.loopGroups = loops;
        this.invalidate();
    }

    /**
     * Calcule la géométrie d'une arête, tronquée aux frontières des nœuds afin
     * que le trait ne passe jamais « sous » les formes.
     * @param {number} curvature courbure relative (0 = droite).
     */
    computePath(source, target, style, curvature) {
        const dx = target.x - source.x;
        const dy = target.y - source.y;
        const distance = Math.hypot(dx, dy) || 0.0001;
        const angle = Math.atan2(dy, dx);
        const inset = 4;

        const startOffset = source.radius + inset;
        const endOffset = target.radius + inset;

        const x1 = source.x + Math.cos(angle) * startOffset;
        const y1 = source.y + Math.sin(angle) * startOffset;
        const x2 = target.x - Math.cos(angle) * endOffset;
        const y2 = target.y - Math.sin(angle) * endOffset;

        // Un groupe d'arêtes parallèles est toujours courbé : c'est la seule façon
        // de les distinguer sans multiplier les styles de tracé.
        const effectiveStyle = style;

        if (effectiveStyle === 'Orthogonal') {
            const midX = (x1 + x2) / 2;
            return {
                type: 'orthogonal',
                points: [
                    { x: x1, y: y1 },
                    { x: midX, y: y1 },
                    { x: midX, y: y2 },
                    { x: x2, y: y2 }
                ],
                label: { x: midX, y: (y1 + y2) / 2 },
                angle
            };
        }

        if (effectiveStyle === 'Curved') {
            const mx = (x1 + x2) / 2;
            const my = (y1 + y2) / 2;
            const offset = distance * curvature;
            const cx = mx - Math.sin(angle) * offset;
            const cy = my + Math.cos(angle) * offset;

            return {
                type: 'curve',
                points: [{ x: x1, y: y1 }, { x: cx, y: cy }, { x: x2, y: y2 }],
                label: {
                    // Point milieu d'une courbe de Bézier quadratique.
                    x: 0.25 * x1 + 0.5 * cx + 0.25 * x2,
                    y: 0.25 * y1 + 0.5 * cy + 0.25 * y2
                },
                angle
            };
        }

        return {
            type: 'line',
            points: [{ x: x1, y: y1 }, { x: x2, y: y2 }],
            label: { x: (x1 + x2) / 2, y: (y1 + y2) / 2 },
            angle
        };
    }

    hitTestEdge(gx, gy, tolerance = 6) {
        const tol = Math.max(tolerance, 6 / this.view.zoom);
        let best = null;
        let bestDistance = tol;

        for (const edge of this.edges.values()) {
            if (!edge.visible) {
                continue;
            }

            const geometry = this.edgeGeometry(edge);
            if (!geometry) {
                continue;
            }

            const points = sampleGeometry(geometry);

            let distance = Infinity;
            for (let i = 0; i < points.length - 1; i++) {
                distance = Math.min(
                    distance,
                    distanceToSegment(gx, gy, points[i].x, points[i].y, points[i + 1].x, points[i + 1].y)
                );
            }

            if (distance < bestDistance) {
                bestDistance = distance;
                best = edge;
            }
        }

        return best;
    }

    snap(value) {
        if (!this.options.snapToGrid) {
            return value;
        }

        const size = this.options.gridSize || 24;
        return Math.round(value / size) * size;
    }

    handlePointerDown(event) {
        this.canvas.focus({ preventScroll: true });

        // La capture peut échouer si le pointeur n'est pas connu du navigateur
        // (synthétisé par un outil de test) : sans elle, le glisser reste
        // fonctionnel tant que le curseur survole le canvas.
        try {
            this.canvas.setPointerCapture?.(event.pointerId);
        } catch {
            // Ignoré volontairement.
        }

        const point = this.toGraph(event.clientX, event.clientY);
        const isPanModifier = event.button === 1 || this.spacePressed || event.altKey;

        if (isPanModifier) {
            this.mode = 'pan';
            this.panStart = {
                clientX: event.clientX,
                clientY: event.clientY,
                viewX: this.view.x,
                viewY: this.view.y
            };
            event.preventDefault();
            return;
        }

        if (event.button !== 0) {
            return;
        }

        const node = this.hitTestNode(point.x, point.y);
        if (node) {
            if (node.locked) {
                this.mode = 'pan';
                this.panStart = {
                    clientX: event.clientX,
                    clientY: event.clientY,
                    viewX: this.view.x,
                    viewY: this.view.y
                };
                return;
            }

            // Ctrl/Cmd + glisser depuis un nœud : création d'arête.
            if (event.ctrlKey || event.metaKey) {
                this.mode = 'link';
                this.linkStartId = node.id;
                this.linkCurrent = { x: point.x, y: point.y };
                event.preventDefault();
                return;
            }

            if (event.shiftKey) {
                if (this.selection.has(node.id)) {
                    this.selection.delete(node.id);

                    // Retirer le nœud actif fait promotesre le suivant comme primaire.
                    if (this.primaryNodeId === node.id) {
                        this.primaryNodeId = this.selection.values().next().value ?? null;
                    }
                } else {
                    this.selection.add(node.id);
                    this.primaryNodeId = node.id;
                }
            } else if (!this.selection.has(node.id)) {
                this.selection = new Set([node.id]);
                this.primaryNodeId = node.id;
            }

            this.activeEdgeId = null;
            this.mode = 'dragNode';
            this.activeNodeId = node.id;
            this.dragOffset = { x: node.x - point.x, y: node.y - point.y };

            // Tous les nœuds sélectionnés suivent le déplacement principal.
            if (!this.selection.has(node.id)) {
                this.dragOrigin = new Map([[node.id, { x: node.x, y: node.y }]]);
            } else {
                this.dragOrigin = new Map(
                    [...this.selection]
                        .map(id => this.nodes.get(id))
                        .filter(n => n && !n.locked)
                        .map(n => [n.id, { x: n.x, y: n.y }])
                );
            }

            this.emitSelection();
            event.preventDefault();
            return;
        }

        // Clic sur une arête.
        const edge = this.hitTestEdge(point.x, point.y);
        if (edge) {
            this.mode = 'idle';
            this.activeEdgeId = edge.id;
            this.emitSelection();
            event.preventDefault();
            return;
        }

        // Clic sur le vide.
        if (event.shiftKey) {
            this.mode = 'marquee';
            this.marqueeStart = { x: point.x, y: point.y };
            this.marqueeRect = null;
        } else {
            if (this.selection.size > 0 || this.activeEdgeId) {
                this.selection.clear();
                this.primaryNodeId = null;
                this.activeEdgeId = null;
                this.emitSelection();
            }

            this.mode = 'pan';
            this.panStart = {
                clientX: event.clientX,
                clientY: event.clientY,
                viewX: this.view.x,
                viewY: this.view.y
            };
        }

        event.preventDefault();
    }

    handlePointerMove(event) {
        const point = this.toGraph(event.clientX, event.clientY);

        if (this.mode === 'idle') {
            const hoveredNode = this.hitTestNode(point.x, point.y);
            const nodeId = hoveredNode ? hoveredNode.id : null;

            if (nodeId !== this.hoverNodeId) {
                this.hoverNodeId = nodeId;
                this.hoverEdgeId = nodeId ? null : (this.hitTestEdge(point.x, point.y)?.id ?? null);
                this.canvas.style.cursor = this.cursorFor(this.hoverNodeId, this.hoverEdgeId);
                this.invalidate();
            }

            return;
        }

        if (this.mode === 'pan' && this.panStart) {
            this.view.x = this.panStart.viewX + (event.clientX - this.panStart.clientX) * this.options.panSpeed;
            this.view.y = this.panStart.viewY + (event.clientY - this.panStart.clientY) * this.options.panSpeed;
            this.invalidate();
            return;
        }

        if (this.mode === 'dragNode' && this.activeNodeId) {
            const targetX = point.x + this.dragOffset.x;
            const targetY = point.y + this.dragOffset.y;

            // Déplacement de l'ensemble des nœuds sélectionnés, en relatif.
            const leaderOrigin = this.dragOrigin.get(this.activeNodeId);
            if (leaderOrigin) {
                const deltaX = targetX - leaderOrigin.x;
                const deltaY = targetY - leaderOrigin.y;

                for (const [id, origin] of this.dragOrigin) {
                    const node = this.nodes.get(id);
                    if (!node || node.locked) {
                        continue;
                    }

                    node.x = this.snap(origin.x + deltaX);
                    node.y = this.snap(origin.y + deltaY);
                }
            }

            this.invalidate();
            return;
        }

        if (this.mode === 'marquee' && this.marqueeStart) {
            this.marqueeRect = {
                x: Math.min(this.marqueeStart.x, point.x),
                y: Math.min(this.marqueeStart.y, point.y),
                width: Math.abs(point.x - this.marqueeStart.x),
                height: Math.abs(point.y - this.marqueeStart.y)
            };
            this.invalidate();
            return;
        }

        if (this.mode === 'link') {
            this.linkCurrent = { x: point.x, y: point.y };
            const hovered = this.hitTestNode(point.x, point.y);
            this.hoverNodeId = hovered ? hovered.id : null;
            this.canvas.style.cursor = hovered ? 'crosshair' : 'crosshair';
            this.invalidate();
        }
    }

    handlePointerUp(event) {
        const point = this.toGraph(event.clientX, event.clientY);
        const previousMode = this.mode;

        // Le nœud promu reste primaire tant que la sélection ne change pas de nature.
        if (previousMode === 'dragNode' && this.activeNodeId) {
            const moved = [...this.dragOrigin.keys()];
            const node = this.nodes.get(this.activeNodeId);
            const origin = this.dragOrigin.get(this.activeNodeId);

            // Un simple clic ne remonte pas de déplacement : on compare avec la
            // position d'origine pour ne notifier que les vrais mouvements.
            if (node && origin && (Math.abs(node.x - origin.x) > 0.01 || Math.abs(node.y - origin.y) > 0.01)) {
                // Les positions définitives sont renvoyées à C#, qui fera foi.
                this.emit('NotifyNodeMoved', this.activeNodeId, node.x, node.y, moved);
            }

            this.dragOrigin = new Map();
        }

        if (previousMode === 'marquee' && this.marqueeRect) {
            const rect = this.marqueeRect;

            for (const node of this.nodes.values()) {
                if (!node.visible || node.locked) {
                    continue;
                }

                // nodeExtent tient compte de la largeur réelle des formes non circulaires.
                const half = this.nodeExtent(node);

                if (node.x + half >= rect.x
                    && node.x - half <= rect.x + rect.width
                    && node.y + half >= rect.y
                    && node.y - half <= rect.y + rect.height) {
                    this.selection.add(node.id);
                }
            }

            // Le premier nœud capturé sert de primaire pour l'anneau plein.
            this.primaryNodeId = this.selection.values().next().value ?? null;
            this.emitSelection();
        }

        if (previousMode === 'link' && this.linkStartId) {
            const target = this.hitTestNode(point.x, point.y);

            // Relâcher sur le nœud de départ crée une boucle, si le mode
            // pseudographe est autorisé.
            const isLoop = target && target.id === this.linkStartId;

            if (target && (target.id !== this.linkStartId || this.options.allowSelfLoops)) {
                this.emit('NotifyLinkRequested', this.linkStartId, target.id, !!isLoop);
            }

            this.linkStartId = null;
            this.linkCurrent = null;
        }

        this.mode = 'idle';
        this.activeNodeId = null;
        this.marqueeStart = null;
        this.marqueeRect = null;
        this.panStart = null;
        this.canvas.style.cursor = 'default';
        this.invalidate();
    }

    handleWheel(event) {
        event.preventDefault();

        if (event.ctrlKey || event.metaKey) {
            // Zoom précis (pincement / Ctrl+molette).
            const factor = Math.pow(0.999, event.deltaY);
            this.zoomAt(event.clientX, event.clientY, factor);
            return;
        }

        const delta = event.deltaMode === 1 ? event.deltaY * 16 : event.deltaY;
        if (event.shiftKey) {
            this.view.x -= (event.deltaY || delta);
            this.invalidate();
            return;
        }

        const factor = Math.pow(0.9995, -delta);
        this.zoomAt(event.clientX, event.clientY, clamp(factor, 0.5, 2));
    }

    handleDoubleClick(event) {
        const point = this.toGraph(event.clientX, event.clientY);
        const node = this.hitTestNode(point.x, point.y);

        if (node) {
            this.emit('NotifyNodeDoubleClicked', node.id);
            return;
        }

        this.emit('NotifyCanvasDoubleClicked', this.snap(point.x), this.snap(point.y));
    }

    handleContextMenu(event) {
        event.preventDefault();

        const point = this.toGraph(event.clientX, event.clientY);
        const rect = this.canvas.getBoundingClientRect();
        const node = this.hitTestNode(point.x, point.y);
        const edge = node ? null : this.hitTestEdge(point.x, point.y);

        // Position du menu, arrondie en pixels et ramenée dans le canvas : un
        // clic près du bord droit ou bas ne doit pas faire déborder le menu.
        // La marge est la taille maximale du menu, minorée de 8 px de sécurité.
        const margin = Math.max(this.options.popupMargin - 8, 0);
        const maxX = Math.max(0, rect.width - margin);
        const maxY = Math.max(0, rect.height - margin);
        const offsetX = clamp(event.clientX - rect.left, 0, maxX);
        const offsetY = clamp(event.clientY - rect.top, 0, maxY);

        this.emit(
            'NotifyContextMenu',
            node ? node.id : (edge ? edge.id : null),
            node ? 'node' : (edge ? 'edge' : 'canvas'),
            Math.round(point.x),
            Math.round(point.y),
            Math.round(offsetX),
            Math.round(offsetY)
        );
    }

    handleKeyDown(event) {
        if (event.code === 'Space') {
            this.spacePressed = true;
            this.canvas.style.cursor = 'grab';
        }

        this.emit('NotifyKeyDown', event.key, event.ctrlKey || event.metaKey, event.shiftKey);
    }

    handleKeyUp(event) {
        if (event.code === 'Space') {
            this.spacePressed = false;
            this.canvas.style.cursor = 'default';
        }
    }

    cursorFor(nodeId, edgeId) {
        if (nodeId) {
            const node = this.nodes.get(nodeId);
            if (node && node.locked) {
                return 'not-allowed';
            }

            return 'grab';
        }

        return edgeId ? 'pointer' : 'default';
    }

    /* ------------------------------------------------------------- interop C# */

    emit(method, ...args) {
        if (!this.dotNetRef) {
            return;
        }

        try {
            this.dotNetRef.invokeMethodAsync(method, ...args).catch(() => {
                // Le circuit est fermé : plus rien à faire.
            });
        } catch {
            // Idem.
        }
    }

    emitSelection() {
        // Le nœud primaire est envoyé en premier : C# en fait le premier de sa
        // sélection et l'inspecteur affiche ainsi l'élément réellement actif.
        const ordered = this.primaryNodeId && this.selection.has(this.primaryNodeId)
            ? [this.primaryNodeId, ...[...this.selection].filter(id => id !== this.primaryNodeId)]
            : [...this.selection];

        this.emit('NotifySelectionChanged', ordered, this.activeEdgeId);
        this.invalidate();
    }

    /* ------------------------------------------------------------------ rendu */

    render() {
        const ctx = this.ctx;
        const theme = this.options.theme;

        ctx.save();
        ctx.setTransform(this.dpr, 0, 0, this.dpr, 0, 0);
        ctx.fillStyle = theme.background;
        ctx.fillRect(0, 0, this.width, this.height);

        if (this.options.showGrid) {
            this.renderGrid(ctx);
        }

        ctx.translate(this.view.x, this.view.y);
        ctx.scale(this.view.zoom, this.view.zoom);

        // Les largeurs de trait restent constantes à l'écran.
        ctx.lineCap = 'round';
        ctx.lineJoin = 'round';

        this.renderEdges(ctx);
        this.renderLinkPreview(ctx);
        this.renderNodes(ctx);
        this.renderMarquee(ctx);

        // Par-dessus tout : le cadre de sélection multiple, à taille écran fixe.
        this.renderSelectionFrame(ctx);

        ctx.restore();
    }

    /**
     * Cadre englobant l'ensemble des nœuds sélectionnés, avec le nombre d'éléments.
     * N'est tracé que si la sélection porte sur plusieurs nœuds : pour un nœud
     * unique, l'anneau de sélection est déjà suffisamment explicite.
     */
    renderSelectionFrame(ctx) {
        if (this.selection.size < 2) {
            return;
        }

        let minX = Infinity;
        let minY = Infinity;
        let maxX = -Infinity;
        let maxY = -Infinity;
        let count = 0;

        for (const id of this.selection) {
            const node = this.nodes.get(id);
            if (!node || !node.visible) {
                continue;
            }

            const half = this.nodeExtent(node);
            minX = Math.min(minX, node.x - half);
            minY = Math.min(minY, node.y - half);
            maxX = Math.max(maxX, node.x + half);
            maxY = Math.max(maxY, node.y + half);
            count++;
        }

        if (count < 2) {
            return;
        }

        const scale = 1 / this.view.zoom;
        const pad = 12 * scale;
        const x = minX - pad;
        const y = minY - pad;
        const w = maxX - minX + pad * 2;
        const h = maxY - minY + pad * 2;
        const color = this.options.selectionColor;

        ctx.save();
        ctx.strokeStyle = color;
        ctx.globalAlpha = 0.75;
        ctx.lineWidth = 1.5 * scale;
        ctx.setLineDash([10 * scale, 6 * scale]);

        // Coins arrondis, tracés à la main pour rester net à tous les zooms.
        const r = Math.min(8 * scale, w / 4, h / 4);
        ctx.beginPath();
        ctx.moveTo(x + r, y);
        ctx.lineTo(x + w - r, y);
        ctx.quadraticCurveTo(x + w, y, x + w, y + r);
        ctx.lineTo(x + w, y + h - r);
        ctx.quadraticCurveTo(x + w, y + h, x + w - r, y + h);
        ctx.lineTo(x + r, y + h);
        ctx.quadraticCurveTo(x, y + h, x, y + h - r);
        ctx.lineTo(x, y + r);
        ctx.quadraticCurveTo(x, y, x + r, y);
        ctx.closePath();
        ctx.stroke();
        ctx.restore();

        // Pastille indiquant le nombre d'éléments sélectionnés.
        const label = String(count);
        const fontSize = 11 * scale;
        ctx.save();
        ctx.font = `700 ${fontSize}px ${this.options.fontFamily}`;
        const metrics = ctx.measureText(label);
        const boxW = metrics.width + 12 * scale;
        const boxH = fontSize + 7 * scale;
        const boxX = x;
        const boxY = y - boxH - 3 * scale;

        ctx.fillStyle = color;
        ctx.beginPath();
        const br = boxH / 2;
        ctx.moveTo(boxX + br, boxY);
        ctx.lineTo(boxX + boxW - br, boxY);
        ctx.arcTo(boxX + boxW, boxY, boxX + boxW, boxY + br, br);
        ctx.lineTo(boxX + boxW, boxY + boxH - br);
        ctx.arcTo(boxX + boxW, boxY + boxH, boxX + boxW - br, boxY + boxH, br);
        ctx.lineTo(boxX + br, boxY + boxH);
        ctx.arcTo(boxX, boxY + boxH, boxX, boxY + boxH - br, br);
        ctx.lineTo(boxX, boxY + br);
        ctx.arcTo(boxX, boxY, boxX + br, boxY, br);
        ctx.closePath();
        ctx.fill();

        ctx.fillStyle = '#0f172a';
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.fillText(label, boxX + boxW / 2, boxY + boxH / 2);
        ctx.restore();
    }

    /** Demi-dimension du nœud, selon sa forme (le rectangle est plus large). */
    nodeExtent(node) {
        return node.shape === 'RoundedRectangle' ? node.radius * 1.25 : node.radius;
    }

    renderGrid(ctx) {
        const size = this.options.gridSize || 24;
        const zoom = this.view.zoom;

        // Deux niveaux de grille pour conserver une lisibilité à tous les zooms.
        const minor = size;
        let major = size * 5;

        while (major * zoom < 40) {
            major *= 5;
        }

        while (major * zoom > 400) {
            major /= 5;
        }

        const startX = (-this.view.x) / zoom;
        const startY = (-this.view.y) / zoom;
        const endX = (this.width - this.view.x) / zoom;
        const endY = (this.height - this.view.y) / zoom;

        const drawLines = (step, color, lineWidth) => {
            ctx.beginPath();
            ctx.strokeStyle = color;
            ctx.lineWidth = lineWidth / zoom;

            const firstX = Math.floor(startX / step) * step;
            for (let x = firstX; x <= endX; x += step) {
                ctx.moveTo(x, startY);
                ctx.lineTo(x, endY);
            }

            const firstY = Math.floor(startY / step) * step;
            for (let y = firstY; y <= endY; y += step) {
                ctx.moveTo(startX, y);
                ctx.lineTo(endX, y);
            }

            ctx.stroke();
        };

        drawLines(minor, this.options.theme.grid, 1);
        drawLines(major, this.options.theme.gridStrong, 1);
    }

    renderEdges(ctx) {
        for (const edge of this.edges.values()) {
            if (!edge.visible) {
                continue;
            }

            const geometry = this.edgeGeometry(edge);
            if (!geometry) {
                continue;
            }

            const isSelected = edge.id === this.activeEdgeId;
            const points = geometry.points;

            const tracePath = () => {
                ctx.beginPath();
                ctx.moveTo(points[0].x, points[0].y);

                if (geometry.type === 'line') {
                    ctx.lineTo(points[1].x, points[1].y);
                } else if (geometry.type === 'curve') {
                    ctx.quadraticCurveTo(points[1].x, points[1].y, points[2].x, points[2].y);
                } else {
                    for (let i = 1; i < points.length; i++) {
                        ctx.lineTo(points[i].x, points[i].y);
                    }
                }
            };

            // Halo doré : résultat d'un calcul effectué en C# (chemin le plus court…).
            if (edge.highlighted) {
                ctx.save();
                tracePath();
                ctx.strokeStyle = '#fbbf24';
                ctx.globalAlpha = 0.9;
                ctx.lineWidth = (edge.width + 9) / this.view.zoom;
                ctx.shadowColor = '#fbbf24';
                ctx.shadowBlur = 16;
                ctx.stroke();
                ctx.restore();
            }

            // Halo pour l'arête sélectionnée ou survolée.
            if (isSelected || edge.id === this.hoverEdgeId) {
                ctx.save();
                tracePath();
                ctx.strokeStyle = this.options.selectionColor;
                ctx.globalAlpha = isSelected ? 0.85 : 0.35;
                ctx.lineWidth = (edge.width + 7) / this.view.zoom;
                ctx.stroke();
                ctx.restore();
            }

            ctx.save();
            tracePath();
            ctx.strokeStyle = edge.color;
            ctx.lineWidth = edge.width / this.view.zoom;
            ctx.setLineDash(edge.dashed ? [6 / this.view.zoom, 5 / this.view.zoom] : []);
            ctx.stroke();
            ctx.restore();

            if (edge.directed && geometry.type !== 'orthogonal') {
                const tip = points[points.length - 1];
                this.renderArrowHead(ctx, tip, geometry.angle, edge.color, edge.width / this.view.zoom);
            }

            if (edge.label) {
                this.renderEdgeLabel(ctx, geometry.label, edge.label, isSelected);
            }
        }
    }

    renderArrowHead(ctx, tip, angle, color, lineWidth) {
        const size = Math.max(8, lineWidth * 4.5);
        ctx.save();
        ctx.translate(tip.x, tip.y);
        ctx.rotate(angle);
        ctx.beginPath();
        ctx.moveTo(0, 0);
        ctx.lineTo(-size, size * 0.42);
        ctx.lineTo(-size, -size * 0.42);
        ctx.closePath();
        ctx.fillStyle = color;
        ctx.fill();
        ctx.restore();
    }

    renderEdgeLabel(ctx, position, text, highlighted) {
        const scale = 1 / this.view.zoom;
        const fontSize = 11 * scale;
        ctx.save();
        ctx.font = `600 ${fontSize}px ${this.options.fontFamily}`;
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';

        const metrics = ctx.measureText(text);
        const paddingX = 5 * scale;
        const height = fontSize + 6 * scale;
        const width = metrics.width + paddingX * 2;

        ctx.fillStyle = highlighted ? this.options.selectionColor : 'rgba(15, 23, 42, 0.85)';
        ctx.beginPath();
        const r = 4 * scale;
        const x = position.x - width / 2;
        const y = position.y - height / 2;

        // Rectangle arrondi simple.
        ctx.moveTo(x + r, y);
        ctx.lineTo(x + width - r, y);
        ctx.quadraticCurveTo(x + width, y, x + width, y + r);
        ctx.lineTo(x + width, y + height - r);
        ctx.quadraticCurveTo(x + width, y + height, x + width - r, y + height);
        ctx.lineTo(x + r, y + height);
        ctx.quadraticCurveTo(x, y + height, x, y + height - r);
        ctx.lineTo(x, y + r);
        ctx.quadraticCurveTo(x, y, x + r, y);
        ctx.closePath();
        ctx.fill();

        ctx.fillStyle = highlighted ? '#0f172a' : '#e2e8f0';
        ctx.fillText(text, position.x, position.y);
        ctx.restore();
    }

    renderLinkPreview(ctx) {
        if (this.mode !== 'link' || !this.linkStartId || !this.linkCurrent) {
            return;
        }

        const source = this.nodes.get(this.linkStartId);
        if (!source) {
            return;
        }

        const target = this.nodes.get(this.hoverNodeId);
        const dx = this.linkCurrent.x - source.x;
        const dy = this.linkCurrent.y - source.y;
        const distance = Math.hypot(dx, dy) || 0.0001;
        const angle = Math.atan2(dy, dx);
        const end = target
            ? { x: target.x - Math.cos(angle) * target.radius, y: target.y - Math.sin(angle) * target.radius }
            : this.linkCurrent;

        ctx.save();
        ctx.beginPath();
        ctx.moveTo(source.x + Math.cos(angle) * source.radius, source.y + Math.sin(angle) * source.radius);
        ctx.lineTo(end.x, end.y);
        ctx.strokeStyle = this.options.selectionColor;
        ctx.lineWidth = 2 / this.view.zoom;
        ctx.setLineDash([8 / this.view.zoom, 6 / this.view.zoom]);
        ctx.stroke();
        ctx.restore();

        this.renderArrowHead(ctx, end, angle, this.options.selectionColor, 2 / this.view.zoom);
    }

    renderNodes(ctx) {
        for (const node of this.nodes.values()) {
            if (!node.visible) {
                continue;
            }

            const isSelected = this.selection.has(node.id);
            const isHovered = this.hoverNodeId === node.id;
            const isActive = this.activeNodeId === node.id;

            // Nœud « primaire » : anneau plein plutôt que dashed.
            const isPrimary = this.primaryNodeId === node.id;

            if (node.highlighted) {
                ctx.save();
                this.traceNode(ctx, node);
                ctx.strokeStyle = '#fbbf24';
                ctx.lineWidth = 5 / this.view.zoom;
                ctx.shadowColor = '#fbbf24';
                ctx.shadowBlur = 22;
                ctx.stroke();
                ctx.restore();
            }

            ctx.save();
            this.traceNode(ctx, node);

            if (isHovered || isActive) {
                ctx.shadowColor = 'rgba(56, 189, 248, 0.55)';
                ctx.shadowBlur = 18;
            }

            ctx.fillStyle = node.fill;
            ctx.fill();
            ctx.restore();

            ctx.save();
            this.traceNode(ctx, node);
            ctx.strokeStyle = node.stroke;
            ctx.lineWidth = this.options.nodeBorderWidth / this.view.zoom;
            ctx.stroke();

            if (node.locked) {
                // Cadre pointillé pour signaler un nœud verrouillé.
                ctx.setLineDash([3 / this.view.zoom, 3 / this.view.zoom]);
                ctx.strokeStyle = 'rgba(255,255,255,0.6)';
                ctx.lineWidth = 1 / this.view.zoom;
                ctx.stroke();
            }

            ctx.restore();

            if (node.label) {
                this.renderNodeLabel(ctx, node);
            }

            // La sélection est tracée après le nœud, sinon le remplissage la masque.
            if (isSelected) {
                this.renderNodeSelection(ctx, node, isPrimary);
            }
        }
    }

    /**
     * Feedback de sélection d'un nœud : lueur extérieure puis anneau dashed.
     * @param {boolean} isPrimary nœud actif au sein d'un groupe (anneau plein).
     */
    renderNodeSelection(ctx, node, isPrimary) {
        const scale = 1 / this.view.zoom;
        const color = this.options.selectionColor;

        // Halo extérieur : visible même pour des formes darkest sur fond clair.
        ctx.save();
        this.traceNode(ctx, node);
        ctx.strokeStyle = color;
        ctx.globalAlpha = 0.35;
        ctx.lineWidth = 9 * scale;
        ctx.shadowColor = color;
        ctx.shadowBlur = 16;
        ctx.stroke();
        ctx.restore();

        // Anneau dashed : distingue la sélection du simple survol.
        ctx.save();
        this.traceNode(ctx, node);
        ctx.strokeStyle = color;
        ctx.lineWidth = 2.5 * scale;
        ctx.setLineDash(isPrimary ? [] : [6 * scale, 5 * scale]);
        ctx.stroke();
        ctx.restore();
    }

    traceNode(ctx, node) {
        const r = node.radius;
        ctx.beginPath();

        switch (node.shape) {
            case 'Square':
                ctx.rect(node.x - r, node.y - r, r * 2, r * 2);
                break;
            case 'RoundedRectangle': {
                const w = r * 1.25;
                const h = r * 0.8;
                const radius = r * 0.35;
                ctx.moveTo(node.x - w + radius, node.y - h);
                ctx.lineTo(node.x + w - radius, node.y - h);
                ctx.quadraticCurveTo(node.x + w, node.y - h, node.x + w, node.y - h + radius);
                ctx.lineTo(node.x + w, node.y + h - radius);
                ctx.quadraticCurveTo(node.x + w, node.y + h, node.x + w - radius, node.y + h);
                ctx.lineTo(node.x - w + radius, node.y + h);
                ctx.quadraticCurveTo(node.x - w, node.y + h, node.x - w, node.y + h - radius);
                ctx.lineTo(node.x - w, node.y - h + radius);
                ctx.quadraticCurveTo(node.x - w, node.y - h, node.x - w + radius, node.y - h);
                ctx.closePath();
                break;
            }
            case 'Diamond':
                ctx.moveTo(node.x, node.y - r);
                ctx.lineTo(node.x + r, node.y);
                ctx.lineTo(node.x, node.y + r);
                ctx.lineTo(node.x - r, node.y);
                ctx.closePath();
                break;
            case 'Triangle':
                ctx.moveTo(node.x, node.y - r);
                ctx.lineTo(node.x + r * 0.866, node.y + r * 0.5);
                ctx.lineTo(node.x - r * 0.866, node.y + r * 0.5);
                ctx.closePath();
                break;
            case 'Hexagon':
                for (let i = 0; i < 6; i++) {
                    const angle = (Math.PI / 3) * i;
                    const px = node.x + Math.cos(angle) * r;
                    const py = node.y + Math.sin(angle) * r * 0.866;
                    if (i === 0) {
                        ctx.moveTo(px, py);
                    } else {
                        ctx.lineTo(px, py);
                    }
                }
                ctx.closePath();
                break;
            case 'Circle':
            default:
                ctx.arc(node.x, node.y, r, 0, Math.PI * 2);
                break;
        }
    }

    renderNodeLabel(ctx, node) {
        // Le texte garde une taille lisible : on le met à l'échelle inverse du zoom
        // uniquement au-delà d'un seuil, sinon les gros nœuds deviennent illisibles.
        const zoomCompensation = clamp(1 / this.view.zoom, 0.6, 1.6);
        const fontSize = node.fontSize * zoomCompensation;

        ctx.save();
        ctx.font = `600 ${fontSize}px ${this.options.fontFamily}`;
        ctx.fillStyle = node.textColor;
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';

        const maxWidth = node.radius * (node.shape === 'RoundedRectangle' ? 2.1 : 1.7);
        const lines = this.wrapText(ctx, node.label, maxWidth);

        const lineHeight = fontSize * 1.25;
        const startY = node.y - ((lines.length - 1) * lineHeight) / 2;

        lines.forEach((line, index) => {
            ctx.fillText(line, node.x, startY + index * lineHeight);
        });

        ctx.restore();
    }

    wrapText(ctx, text, maxWidth) {
        const words = String(text).split(/\s+/).filter(Boolean);
        if (words.length === 0) {
            return [''];
        }

        const lines = [];
        let current = words[0];

        for (let i = 1; i < words.length; i++) {
            const candidate = `${current} ${words[i]}`;
            if (ctx.measureText(candidate).width <= maxWidth) {
                current = candidate;
            } else {
                lines.push(current);
                current = words[i];
            }
        }

        lines.push(current);

        // Découpe de sécurité des mots trop longs.
        const result = [];
        for (const line of lines) {
            if (ctx.measureText(line).width <= maxWidth) {
                result.push(line);
                continue;
            }

            let chunk = '';
            for (const char of line) {
                if (ctx.measureText(chunk + char).width > maxWidth && chunk) {
                    result.push(chunk);
                    chunk = char;
                } else {
                    chunk += char;
                }
            }

            if (chunk) {
                result.push(chunk);
            }
        }

        return result;
    }

    renderMarquee(ctx) {
        if (this.mode !== 'marquee' || !this.marqueeRect) {
            return;
        }

        const rect = this.marqueeRect;
        ctx.save();
        ctx.fillStyle = 'rgba(56, 189, 248, 0.12)';
        ctx.strokeStyle = this.options.selectionColor;
        ctx.lineWidth = 1 / this.view.zoom;
        ctx.setLineDash([4 / this.view.zoom, 4 / this.view.zoom]);
        ctx.fillRect(rect.x, rect.y, rect.width, rect.height);
        ctx.strokeRect(rect.x, rect.y, rect.width, rect.height);
        ctx.restore();
    }

    /* ------------------------------------------------------------- export PNG */

    toDataUrl(type = 'image/png', quality = 0.92) {
        // Redessine à taille de buffer pour un export net.
        return this.canvas.toDataURL(type, quality);
    }
}

/* ------------------------------------------------------- API publique (ES module) */

export function create(canvas, dotNetRef, options) {
    const existing = instances.get(canvas);
    if (existing) {
        existing.dispose();
    }

    const instance = new GraphCanvas(canvas, dotNetRef, options || {});
    instances.set(canvas, instance);
    return instance.id;
}

export function dispose(canvas) {
    const instance = instances.get(canvas);
    if (instance) {
        instance.dispose();
        instances.delete(canvas);
    }
}

function useInstance(canvas, action) {
    const instance = instances.get(canvas);
    if (!instance) {
        return null;
    }

    return action(instance);
}

export function setGraph(canvas, payload) {
    return useInstance(canvas, instance => {
        instance.setGraph(payload);
        return true;
    });
}

export function applyChanges(canvas, changes) {
    return useInstance(canvas, instance => {
        instance.applyChanges(changes);
        return true;
    });
}

export function setOptions(canvas, options) {
    return useInstance(canvas, instance => {
        instance.setOptions(options);
        return true;
    });
}

export function setSelection(canvas, nodeIds, edgeId) {
    return useInstance(canvas, instance => {
        instance.setSelection(nodeIds, edgeId);
        return true;
    });
}

export function clearSelection(canvas) {
    return useInstance(canvas, instance => {
        instance.clearSelection();
        return true;
    });
}

export function fitToView(canvas, padding) {
    return useInstance(canvas, instance => {
        instance.fitToView(padding);
        return instance.getView();
    });
}

export function zoomBy(canvas, factor) {
    return useInstance(canvas, instance => {
        instance.zoomBy(factor);
        return instance.getView();
    });
}

export function setZoom(canvas, zoom) {
    return useInstance(canvas, instance => {
        instance.setZoom(zoom);
        return instance.getView();
    });
}

export function getView(canvas) {
    return useInstance(canvas, instance => instance.getView());
}

export function setView(canvas, view) {
    return useInstance(canvas, instance => {
        instance.setView(view);
        return instance.getView();
    });
}

export function resetView(canvas) {
    return useInstance(canvas, instance => {
        instance.resetView();
        return instance.getView();
    });
}

export function focusNode(canvas, id, zoom) {
    return useInstance(canvas, instance => {
        instance.focusNode(id, zoom);
        return instance.getView();
    });
}

export function resize(canvas) {
    return useInstance(canvas, instance => {
        instance.resize();
        return true;
    });
}

export function toDataUrl(canvas, type, quality) {
    return useInstance(canvas, instance => instance.toDataUrl(type, quality));
}

/// <summary>Télécharge le canvas sous forme de fichier image.</summary>
export function downloadPng(canvas, fileName = 'graphe.png', type = 'image/png', quality = 0.92) {
    const url = useInstance(canvas, instance => instance.toDataUrl(type, quality));

    if (!url) {
        return false;
    }

    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();
    return true;
}

/** Géométrie calculée de chaque arête : diagnostic du rendu des liaisons parallèles. */
export function getEdgeGeometries(canvas) {
    return useInstance(canvas, instance => [...instance.edges.values()].map(edge => {
        const geometry = instance.edgeGeometry(edge);
        return {
            id: edge.id,
            sourceId: edge.sourceId,
            targetId: edge.targetId,
            parallelOffset: Number(instance.parallelOffset(edge).toFixed(4)),
            isLoop: edge.sourceId === edge.targetId,
            type: geometry ? geometry.type : null,
            midX: geometry ? Math.round(geometry.label.x) : null,
            midY: geometry ? Math.round(geometry.label.y) : null
        };
    }));
}

/**
 * Positionne un menu flottant dans le canvas en mesurant sa taille réelle.
 * Indispensable près des bords : une marge estimée serait fausse, et un menu
 * débordant hors du canvas finit sous la barre d'outils.
 */
export function placePopup(canvas, popup, x, y) {
    if (!popup) {
        return null;
    }

    const rect = canvas.getBoundingClientRect();
    const width = popup.offsetWidth;
    const height = popup.offsetHeight;

    const left = clamp(x, 0, Math.max(0, rect.width - width));
    const top = clamp(y, 0, Math.max(0, rect.height - height));

    popup.style.left = `${Math.round(left)}px`;
    popup.style.top = `${Math.round(top)}px`;
    popup.style.visibility = 'visible';

    return { left: Math.round(left), top: Math.round(top) };
}

export function screenToGraph(canvas, offsetX, offsetY) {
    return useInstance(canvas, instance => ({
        x: (offsetX - instance.view.x) / instance.view.zoom,
        y: (offsetY - instance.view.y) / instance.view.zoom
    }));
}

export function getCenter(canvas) {
    return useInstance(canvas, instance => ({
        x: (instance.width / 2 - instance.view.x) / instance.view.zoom,
        y: (instance.height / 2 - instance.view.y) / instance.view.zoom
    }));
}

export function getNodeScreenPosition(canvas, id) {
    return useInstance(canvas, instance => {
        const node = instance.nodes.get(id);
        if (!node) {
            return null;
        }

        const rect = instance.canvas.getBoundingClientRect();
        const screen = instance.toScreen(node.x, node.y);
        return {
            x: rect.left + screen.x,
            y: rect.top + screen.y,
            radius: node.radius * instance.view.zoom
        };
    });
}