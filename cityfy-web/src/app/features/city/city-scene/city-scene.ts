import {
  AfterViewInit,
  Component,
  DestroyRef,
  ElementRef,
  NgZone,
  effect,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import * as THREE from 'three';
import { MapControls } from 'three/addons/controls/MapControls.js';
import { City } from '../../../core/api/api.models';
import { CURB, CityLayout, CityTemplate, Furniture, PlacedBuilding, Point, Streets, computeCityLayout } from '../layout/city-layout';
import { Stripe, roadMarkings } from '../layout/streets';
import { ThemeService } from '../../../core/theme/theme.service';

/** Colors and lights of the scene: day for the light theme, night for the dark one. */
interface Palette {
  sky: string;
  ground: string; // open ground between districts reads as parks/countryside
  asphalt: string;
  sidewalk: string;
  plateBase: string; // block plates: district color faded towards this
  plateFade: number;
  grass: string;
  paint: string;
  metal: string;
  hemi: [string, string, number];
  sun: [string, number];
  lampGlow: number; // emissive intensity of the lamp heads
  lampPools: boolean; // light pools under the lamps
  buildingGlow: number; // buildings slightly emissive (lit windows) at night
  label: { background: string; text: string };
}

const DAY: Palette = {
  sky: '#eee9e1',
  ground: '#d3dbc2',
  asphalt: '#a5a199',
  sidewalk: '#cdc8be',
  plateBase: '#e4ddcf',
  plateFade: 0.62,
  grass: '#a9c48f',
  paint: '#f7f5ef',
  metal: '#4a4d52',
  hemi: ['#ffffff', '#b8b0a0', 0.9],
  sun: ['#ffffff', 1.2],
  lampGlow: 0.4,
  lampPools: false,
  buildingGlow: 0,
  label: { background: 'rgba(255, 255, 255, 0.85)', text: '#2b2b2b' },
};

const NIGHT: Palette = {
  sky: '#0e1117',
  ground: '#18201a',
  asphalt: '#2a2c31',
  sidewalk: '#45474d',
  plateBase: '#2b2d33',
  plateFade: 0.72,
  grass: '#22361f',
  paint: '#bdbcb6',
  metal: '#6a6e75',
  hemi: ['#8ea2c8', '#14161c', 0.6],
  sun: ['#a9bbff', 0.45], // moonlight
  lampGlow: 2.2,
  lampPools: true,
  buildingGlow: 0.16,
  label: { background: 'rgba(22, 25, 31, 0.85)', text: '#ece9e2' },
};

const MINIMAP_MAX = 200; // px, longest side
const MINIMAP_MARGIN = 12; // px from the bottom-left corner
const LAYER_MINIMAP = 1; // camera marker, only in the minimap
const LAYER_LABELS = 2; // district names, only in the main view

/**
 * 3D city (three.js). Map-like navigation: drag to move, right drag to rotate, wheel to zoom (towards
 * the cursor), WASD/arrows to move, Q/E to rotate, double click to fly to a building or a point,
 * minimap to jump anywhere. Buildings are one InstancedMesh (thousands of artists stay fast);
 * streets and squares are the gaps between the block plates. Emits the clicked artist.
 */
@Component({
  selector: 'app-city-scene',
  template: `
    <canvas #canvas tabindex="0"></canvas>
    @if (hovered(); as h) {
      <div class="tooltip" [style.left.px]="h.x" [style.top.px]="h.y">
        <strong>{{ h.building.name }}</strong><br />{{ h.building.genreName }}
      </div>
    }
    <div
      #minimap
      class="minimap"
      title="Clicca per spostarti"
      [style.width.px]="minimapSize().w"
      [style.height.px]="minimapSize().h"
    ></div>
    <div class="controls">
      <button type="button" (click)="frameCity(true)" title="Inquadra tutta la città">⤢ Vista intera</button>
      <button type="button" (click)="showHelp.set(!showHelp())" [class.active]="showHelp()" title="Comandi">?</button>
    </div>
    @if (showHelp()) {
      <div class="help">
        <b>Trascina</b> per spostarti · <b>tasto destro</b> per ruotare · <b>rotella</b> per lo zoom<br />
        <b>WASD</b>/frecce per muoverti · <b>Q/E</b> per ruotare · <b>doppio clic</b> per avvicinarti
      </div>
    }
  `,
  styleUrl: './city-scene.scss',
})
export class CityScene implements AfterViewInit {
  readonly city = input.required<City>();
  readonly template = input<CityTemplate>('organic');
  readonly selected = input<string | null>(null);
  readonly artistSelected = output<string>();

  protected readonly hovered = signal<{ building: PlacedBuilding; x: number; y: number } | null>(null);
  protected readonly minimapSize = signal({ w: MINIMAP_MAX, h: MINIMAP_MAX });
  protected readonly showHelp = signal(true);

  private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  private readonly minimapEl = viewChild.required<ElementRef<HTMLDivElement>>('minimap');
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly zone = inject(NgZone);
  private readonly destroyRef = inject(DestroyRef);

  private renderer?: THREE.WebGLRenderer;
  private camera?: THREE.PerspectiveCamera;
  private readonly mapCamera = new THREE.OrthographicCamera();
  private controls?: MapControls;
  private readonly scene = new THREE.Scene();
  private cityGroup = new THREE.Group();
  private buildingsMesh?: THREE.InstancedMesh;
  private layout?: CityLayout;
  private extent = 20;
  private readonly raycaster = new THREE.Raycaster();
  private readonly pointer = new THREE.Vector2();
  private readonly highlight = new THREE.Mesh(
    new THREE.BoxGeometry(1, 1, 1),
    new THREE.MeshBasicMaterial({ color: '#ffffff', side: THREE.BackSide }),
  );
  private readonly marker = new THREE.Mesh(
    new THREE.ConeGeometry(0.5, 1.4, 3).rotateX(-Math.PI / 2), // points to -z = "forward" once rotated
    new THREE.MeshBasicMaterial({ color: '#d1495b', depthTest: false }),
  );
  private readonly keys = new Set<string>();
  private flight?: { fromTarget: THREE.Vector3; toTarget: THREE.Vector3; fromCam: THREE.Vector3; toCam: THREE.Vector3; start: number; duration: number };

  private readonly theme = inject(ThemeService);
  private readonly hemi = new THREE.HemisphereLight();
  private readonly sun = new THREE.DirectionalLight();
  private layoutOf?: { city: City; template: CityTemplate };

  constructor() {
    // Rebuild the city whenever the data (e.g. the period), the template or the theme (day/night) changes
    effect(() => {
      const city = this.city();
      const template = this.template();
      const palette = this.theme.theme() === 'dark' ? NIGHT : DAY;
      if (this.renderer) this.buildCity(city, template, palette);
    });
    effect(() => this.updateHighlight(this.selected()));
  }

  ngAfterViewInit(): void {
    const canvas = this.canvas().nativeElement;
    const minimap = this.minimapEl().nativeElement;

    this.scene.background = new THREE.Color();
    this.scene.fog = new THREE.Fog('#000000', 60, 220);
    this.sun.position.set(30, 50, 20);
    this.scene.add(this.hemi, this.sun);
    this.highlight.visible = false;
    this.scene.add(this.highlight);
    this.marker.layers.set(LAYER_MINIMAP);
    this.marker.renderOrder = 20;
    this.scene.add(this.marker);

    this.camera = new THREE.PerspectiveCamera(45, 1, 0.5, 4000);
    this.camera.layers.enable(LAYER_LABELS);
    this.mapCamera.layers.enable(LAYER_MINIMAP);
    this.mapCamera.up.set(0, 0, -1); // north (-z) up in the minimap
    this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));

    // Map controls: left drag pans on the ground, right drag rotates, wheel zooms to the cursor
    this.controls = new MapControls(this.camera, canvas);
    this.controls.enableDamping = true;
    this.controls.dampingFactor = 0.08;
    this.controls.screenSpacePanning = false;
    this.controls.zoomToCursor = true;
    this.controls.maxPolarAngle = Math.PI / 2.2; // never below the ground
    this.controls.minDistance = 6;

    this.buildCity(this.city(), this.template(), this.theme.theme() === 'dark' ? NIGHT : DAY);

    // Render loop and input handling outside Angular: no change detection at 60 fps
    this.zone.runOutsideAngular(() => {
      const resize = new ResizeObserver(() => this.resize());
      resize.observe(this.host.nativeElement);

      let down: { x: number; y: number } | null = null;
      const onDown = (e: PointerEvent) => (down = { x: e.clientX, y: e.clientY });
      const onMove = (e: PointerEvent) => this.onPointerMove(e);
      // a click at the end of a drag is a pan, not a selection
      const onClick = (e: MouseEvent) => {
        if (!down || Math.hypot(e.clientX - down.x, e.clientY - down.y) < 5) this.onClick();
      };
      const onDblClick = (e: MouseEvent) => this.onDoubleClick(e);
      const onLeave = () => this.zone.run(() => this.hovered.set(null));
      canvas.addEventListener('pointerdown', onDown);
      canvas.addEventListener('pointermove', onMove);
      canvas.addEventListener('pointerleave', onLeave);
      canvas.addEventListener('click', onClick);
      canvas.addEventListener('dblclick', onDblClick);

      // Minimap: click or drag to move there
      let dragging = false;
      const onMapDown = (e: PointerEvent) => {
        dragging = true;
        minimap.setPointerCapture(e.pointerId);
        this.jumpFromMinimap(e, true);
      };
      const onMapMove = (e: PointerEvent) => dragging && this.jumpFromMinimap(e, false);
      const onMapUp = () => (dragging = false);
      minimap.addEventListener('pointerdown', onMapDown);
      minimap.addEventListener('pointermove', onMapMove);
      minimap.addEventListener('pointerup', onMapUp);

      // Keyboard: anywhere on the page, except while typing
      const onKeyDown = (e: KeyboardEvent) => {
        const t = e.target as HTMLElement | null;
        if (t && (t.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(t.tagName))) return;
        const key = e.key.toLowerCase();
        if (['w', 'a', 's', 'd', 'q', 'e', 'arrowup', 'arrowdown', 'arrowleft', 'arrowright'].includes(key)) {
          this.keys.add(key);
          if (key.startsWith('arrow')) e.preventDefault(); // no page scroll
        }
      };
      const onKeyUp = (e: KeyboardEvent) => this.keys.delete(e.key.toLowerCase());
      const onBlur = () => this.keys.clear();
      window.addEventListener('keydown', onKeyDown);
      window.addEventListener('keyup', onKeyUp);
      window.addEventListener('blur', onBlur);

      const clock = new THREE.Clock();
      let frame = 0;
      const loop = () => {
        frame = requestAnimationFrame(loop);
        const dt = Math.min(clock.getDelta(), 0.1);
        this.animateFlight();
        this.moveWithKeys(dt);
        this.controls!.update();
        this.clampTarget();
        this.render();
      };
      loop();

      this.destroyRef.onDestroy(() => {
        cancelAnimationFrame(frame);
        resize.disconnect();
        canvas.removeEventListener('pointerdown', onDown);
        canvas.removeEventListener('pointermove', onMove);
        canvas.removeEventListener('pointerleave', onLeave);
        canvas.removeEventListener('click', onClick);
        canvas.removeEventListener('dblclick', onDblClick);
        minimap.removeEventListener('pointerdown', onMapDown);
        minimap.removeEventListener('pointermove', onMapMove);
        minimap.removeEventListener('pointerup', onMapUp);
        window.removeEventListener('keydown', onKeyDown);
        window.removeEventListener('keyup', onKeyUp);
        window.removeEventListener('blur', onBlur);
        this.controls?.dispose();
        this.disposeCity();
        this.renderer?.dispose();
      });
    });
  }

  // -------------------------------------------------------------------------
  // City geometry
  // -------------------------------------------------------------------------

  private buildCity(city: City, template: CityTemplate, palette: Palette): void {
    this.disposeCity();
    // the layout changes only with the data or the template (not with the theme): camera kept
    const relayout = !this.layout || this.layoutOf?.city !== city || this.layoutOf?.template !== template;
    if (relayout) {
      this.layout = computeCityLayout(city, template);
      this.layoutOf = { city, template };
    }
    const layout = this.layout!;
    this.cityGroup = new THREE.Group();
    const b = layout.bounds;
    const cx = (b.minX + b.maxX) / 2;
    const cz = (b.minZ + b.maxZ) / 2;
    this.extent = Math.max(layout.width, layout.depth, 30);

    // Sky and lights (day / night)
    (this.scene.background as THREE.Color).set(palette.sky);
    (this.scene.fog as THREE.Fog).color.set(palette.sky);
    this.hemi.color.set(palette.hemi[0]);
    this.hemi.groundColor.set(palette.hemi[1]);
    this.hemi.intensity = palette.hemi[2];
    this.sun.color.set(palette.sun[0]);
    this.sun.intensity = palette.sun[1];

    // Ground (countryside around the city)
    const ground = new THREE.Mesh(
      new THREE.PlaneGeometry(this.extent * 4, this.extent * 4),
      new THREE.MeshStandardMaterial({ color: palette.ground, roughness: 1 }),
    );
    ground.rotation.x = -Math.PI / 2;
    ground.position.set(cx, 0, cz);
    this.cityGroup.add(ground);

    // Asphalt: whole district cells (main roads, streets and squares are what the plates leave free)
    this.cityGroup.add(polygonsMesh(layout.districts.flatMap((d) => d.outlines.map((points) => ({ points, color: palette.asphalt }))), 0.02));
    // Roundabouts: asphalt disc under them (in the radial city the hub is wider than the slices' tips)
    for (const p of layout.plazas) {
      const disc = new THREE.Mesh(new THREE.CircleGeometry(p.radius, 48), new THREE.MeshStandardMaterial({ color: palette.asphalt, roughness: 1 }));
      disc.rotation.x = -Math.PI / 2;
      disc.position.set(p.x, 0.03, p.z);
      this.cityGroup.add(disc);
    }

    // Sidewalks: raised slab with a curb around every sub-block; the plate (pale district color) on top
    const parts = layout.blocks.flatMap((x) => x.parts.map((p) => ({ part: p, color: x.color })));
    this.cityGroup.add(prismMesh(parts.map((x) => x.part.outline), CURB, palette.sidewalk));
    const base = new THREE.Color(palette.plateBase);
    this.cityGroup.add(
      polygonsMesh(
        parts.map((x) => ({ points: x.part.plate, color: '#' + new THREE.Color(x.color).lerp(base, palette.plateFade).getHexString() })),
        CURB + 0.005,
      ),
    );

    // Horizontal markings and street furniture
    this.cityGroup.add(markingsMesh(roadMarkings(layout.streets), palette));
    this.cityGroup.add(furnitureGroup(layout.streets, palette));

    // Squares: roundabout with a green island and a white kerb
    for (const p of layout.plazas) {
      const island = new THREE.Mesh(new THREE.CircleGeometry(p.radius * 0.5, 40), new THREE.MeshStandardMaterial({ color: palette.grass }));
      island.rotation.x = -Math.PI / 2;
      island.position.set(p.x, 0.08, p.z);
      const kerb = new THREE.Mesh(
        new THREE.RingGeometry(p.radius * 0.5, p.radius * 0.56, 40),
        new THREE.MeshStandardMaterial({ color: palette.paint }),
      );
      kerb.rotation.x = -Math.PI / 2;
      kerb.position.set(p.x, 0.09, p.z);
      this.cityGroup.add(island, kerb);
    }

    for (const d of layout.districts) {
      const label = labelSprite(d.name, d.color, d.radius, palette);
      label.position.set(d.x, 8 + d.radius * 0.15, d.z);
      this.cityGroup.add(label);
    }

    // Buildings: one instance per artist, unit box scaled to width x height x width, rotated as the block grid.
    // At night they glow a little in their own color (lit windows).
    const buildings = layout.buildings;
    if (buildings.length > 0) {
      const mesh = new THREE.InstancedMesh(
        new THREE.BoxGeometry(1, 1, 1),
        new THREE.MeshStandardMaterial({ roughness: 0.85, emissive: '#ffffff', emissiveIntensity: 0 }),
        buildings.length,
      );
      if (palette.buildingGlow > 0) {
        // faint warm light on every building, so the city does not disappear in the dark
        const material = mesh.material as THREE.MeshStandardMaterial;
        material.emissive.set('#ffd9a0');
        material.emissiveIntensity = palette.buildingGlow;
      }
      const matrix = new THREE.Matrix4();
      const color = new THREE.Color();
      const up = new THREE.Vector3(0, 1, 0);
      buildings.forEach((x, i) => {
        matrix.compose(
          new THREE.Vector3(x.x, x.height / 2 + CURB, x.z),
          new THREE.Quaternion().setFromAxisAngle(up, x.rotation),
          new THREE.Vector3(x.width, x.height, x.width),
        );
        mesh.setMatrixAt(i, matrix);
        mesh.setColorAt(i, variate(color.set(x.color), i));
      });
      mesh.instanceMatrix.needsUpdate = true;
      this.buildingsMesh = mesh;
      this.cityGroup.add(mesh);
    }

    this.scene.add(this.cityGroup);

    // Minimap: top view of the whole city
    const aspectCity = Math.max(layout.width, 1) / Math.max(layout.depth, 1);
    const w = aspectCity >= 1 ? MINIMAP_MAX : Math.max(100, MINIMAP_MAX * aspectCity);
    const h = aspectCity >= 1 ? Math.max(100, MINIMAP_MAX / aspectCity) : MINIMAP_MAX;
    this.zone.run(() => this.minimapSize.set({ w: Math.round(w), h: Math.round(h) }));
    const half = Math.max(layout.width / 2 / (w / h), layout.depth / 2, 15) * 1.06;
    Object.assign(this.mapCamera, { left: -half * (w / h), right: half * (w / h), top: half, bottom: -half, near: 1, far: 1000 });
    this.mapCamera.updateProjectionMatrix();
    this.mapCamera.position.set(cx, 500, cz);
    this.mapCamera.lookAt(cx, 0, cz);
    this.marker.scale.setScalar(Math.max(half / 12, 1));

    this.controls!.maxDistance = this.extent * 1.6;
    (this.scene.fog as THREE.Fog).near = this.extent * 0.8;
    (this.scene.fog as THREE.Fog).far = this.extent * 3;
    if (relayout) this.frameCity(false);
    this.updateHighlight(this.selected());
  }

  /** Whole city in view (button "Vista intera"). */
  protected frameCity(animated: boolean): void {
    if (!this.layout || !this.camera) return;
    const b = this.layout.bounds;
    const target = new THREE.Vector3((b.minX + b.maxX) / 2, 0, (b.minZ + b.maxZ) / 2);
    const cam = target.clone().add(new THREE.Vector3(0.55, 0.85, 0.75).multiplyScalar(this.extent * 0.75));
    if (animated) this.flyTo(target, cam);
    else {
      this.flight = undefined;
      this.controls!.target.copy(target);
      this.camera.position.copy(cam);
    }
  }

  // -------------------------------------------------------------------------
  // Navigation
  // -------------------------------------------------------------------------

  private flyTo(target: THREE.Vector3, cam: THREE.Vector3, duration = 700): void {
    this.flight = {
      fromTarget: this.controls!.target.clone(),
      toTarget: target,
      fromCam: this.camera!.position.clone(),
      toCam: cam,
      start: performance.now(),
      duration,
    };
  }

  private animateFlight(): void {
    if (!this.flight) return;
    const f = this.flight;
    const t = Math.min((performance.now() - f.start) / f.duration, 1);
    const k = t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2; // ease in-out
    this.controls!.target.lerpVectors(f.fromTarget, f.toTarget, k);
    this.camera!.position.lerpVectors(f.fromCam, f.toCam, k);
    if (t >= 1) this.flight = undefined;
  }

  /** WASD/arrows move on the ground (relative to the view), Q/E rotate around the target. */
  private moveWithKeys(dt: number): void {
    if (this.keys.size === 0 || !this.camera || !this.controls) return;
    this.flight = undefined;
    const target = this.controls.target;
    const offset = this.camera.position.clone().sub(target);
    const forward = new THREE.Vector3(-offset.x, 0, -offset.z).normalize();
    const right = new THREE.Vector3(-forward.z, 0, forward.x);
    const speed = Math.max(offset.length(), 20) * 0.9 * dt;

    const move = new THREE.Vector3();
    if (this.keys.has('w') || this.keys.has('arrowup')) move.add(forward);
    if (this.keys.has('s') || this.keys.has('arrowdown')) move.sub(forward);
    if (this.keys.has('d') || this.keys.has('arrowright')) move.add(right);
    if (this.keys.has('a') || this.keys.has('arrowleft')) move.sub(right);
    if (move.lengthSq() > 0) {
      move.normalize().multiplyScalar(speed);
      target.add(move);
      this.camera.position.add(move);
    }

    const turn = (this.keys.has('q') ? 1 : 0) - (this.keys.has('e') ? 1 : 0);
    if (turn !== 0) {
      offset.applyAxisAngle(new THREE.Vector3(0, 1, 0), turn * 1.4 * dt);
      this.camera.position.copy(target).add(offset);
    }
  }

  /** The target stays over the city (plus a margin): no getting lost in the void. */
  private clampTarget(): void {
    if (!this.layout || !this.controls || !this.camera) return;
    const b = this.layout.bounds;
    const margin = this.extent * 0.15;
    const t = this.controls.target;
    const x = THREE.MathUtils.clamp(t.x, b.minX - margin, b.maxX + margin);
    const z = THREE.MathUtils.clamp(t.z, b.minZ - margin, b.maxZ + margin);
    if (x !== t.x || z !== t.z || t.y !== 0) {
      const delta = new THREE.Vector3(x - t.x, -t.y, z - t.z);
      t.add(delta);
      this.camera.position.add(delta);
    }
  }

  /** Double click: fly close to a building, or to the clicked point of the ground. */
  private onDoubleClick(e: MouseEvent): void {
    const building = this.pick(e);
    const offsetDir = this.camera!.position.clone().sub(this.controls!.target).normalize();
    if (building) {
      const target = new THREE.Vector3(building.x, building.height * 0.4, building.z);
      const distance = Math.max(14, building.height * 3);
      this.flyTo(target.clone().setY(0), target.clone().add(offsetDir.multiplyScalar(distance)));
      this.zone.run(() => this.artistSelected.emit(building.artistId));
      return;
    }
    const point = this.groundPoint(e);
    if (point) {
      const distance = Math.max(this.controls!.minDistance * 3, this.camera!.position.distanceTo(this.controls!.target) * 0.6);
      this.flyTo(point, point.clone().add(offsetDir.multiplyScalar(distance)));
    }
  }

  private jumpFromMinimap(e: PointerEvent, animated: boolean): void {
    const rect = this.minimapEl().nativeElement.getBoundingClientRect();
    const u = (e.clientX - rect.left) / rect.width;
    const v = (e.clientY - rect.top) / rect.height;
    const c = this.mapCamera;
    const point = new THREE.Vector3(
      c.position.x + c.left + u * (c.right - c.left),
      0,
      c.position.z - c.top + v * (c.top - c.bottom), // up in the minimap = -z
    );
    const offset = this.camera!.position.clone().sub(this.controls!.target);
    if (animated) this.flyTo(point, point.clone().add(offset), 450);
    else {
      this.flight = undefined;
      this.controls!.target.copy(point);
      this.camera!.position.copy(point).add(offset);
    }
  }

  // -------------------------------------------------------------------------
  // Rendering
  // -------------------------------------------------------------------------

  private render(): void {
    const renderer = this.renderer!;
    const { clientWidth: w, clientHeight: h } = this.host.nativeElement;
    renderer.setViewport(0, 0, w, h);
    renderer.setScissorTest(false);
    renderer.render(this.scene, this.camera!);

    // Minimap: same scene from above, bottom-left corner, without fog; the marker shows the view
    const size = this.minimapSize();
    const t = this.controls!.target;
    const view = this.camera!.position.clone().sub(t);
    this.marker.position.set(t.x, 50, t.z);
    this.marker.rotation.y = Math.atan2(view.x, view.z); // cone points away from the camera
    const fog = this.scene.fog as THREE.Fog;
    const [near, far] = [fog.near, fog.far];
    fog.near = fog.far = 1e6;
    renderer.setScissorTest(true);
    renderer.setScissor(MINIMAP_MARGIN, MINIMAP_MARGIN, size.w, size.h);
    renderer.setViewport(MINIMAP_MARGIN, MINIMAP_MARGIN, size.w, size.h);
    renderer.render(this.scene, this.mapCamera);
    renderer.setScissorTest(false);
    fog.near = near;
    fog.far = far;
  }

  private onPointerMove(e: PointerEvent): void {
    if (e.buttons !== 0) return; // dragging: no picking
    const hit = this.pick(e);
    const current = this.hovered();
    if (hit !== current?.building || hit) this.zone.run(() => this.hovered.set(hit ? { building: hit, x: e.offsetX, y: e.offsetY } : null));
    this.renderer!.domElement.style.cursor = hit ? 'pointer' : 'grab';
  }

  private onClick(): void {
    const h = this.hovered();
    if (h) this.zone.run(() => this.artistSelected.emit(h.building.artistId));
  }

  private setPointer(e: MouseEvent): void {
    const rect = this.renderer!.domElement.getBoundingClientRect();
    this.pointer.set(((e.clientX - rect.left) / rect.width) * 2 - 1, -((e.clientY - rect.top) / rect.height) * 2 + 1);
    this.raycaster.setFromCamera(this.pointer, this.camera!);
  }

  private pick(e: MouseEvent): PlacedBuilding | null {
    if (!this.buildingsMesh || !this.layout) return null;
    this.setPointer(e);
    const [hit] = this.raycaster.intersectObject(this.buildingsMesh, false);
    return hit?.instanceId !== undefined ? this.layout.buildings[hit.instanceId] : null;
  }

  private groundPoint(e: MouseEvent): THREE.Vector3 | null {
    this.setPointer(e);
    return this.raycaster.ray.intersectPlane(new THREE.Plane(new THREE.Vector3(0, 1, 0), 0), new THREE.Vector3());
  }

  /** White "inverted shell" outline around the selected building. */
  private updateHighlight(artistId: string | null): void {
    const b = artistId ? this.layout?.buildings.find((x) => x.artistId === artistId) : undefined;
    this.highlight.visible = !!b;
    if (b) {
      this.highlight.position.set(b.x, b.height / 2 + CURB, b.z);
      this.highlight.rotation.set(0, b.rotation, 0);
      this.highlight.scale.set(b.width * 1.08, b.height * 1.04, b.width * 1.08);
    }
  }

  private resize(): void {
    const { clientWidth: w, clientHeight: h } = this.host.nativeElement;
    if (!w || !h) return;
    this.camera!.aspect = w / h;
    this.camera!.updateProjectionMatrix();
    this.renderer!.setSize(w, h, false);
  }

  private disposeCity(): void {
    this.scene.remove(this.cityGroup);
    this.cityGroup.traverse((o) => {
      if (o instanceof THREE.Mesh || o instanceof THREE.Sprite) {
        if (o instanceof THREE.Mesh) o.geometry.dispose(); // sprites share one geometry inside three.js
        const material = o.material as THREE.Material & { map?: THREE.Texture | null };
        material.map?.dispose();
        material.dispose();
      }
    });
    this.buildingsMesh = undefined;
  }
}

/** Convex polygons as one flat mesh (fan triangulation, vertex colors, facing up). */
function polygonsMesh(polygons: { points: Point[]; color: string }[], y: number): THREE.Mesh {
  const positions: number[] = [];
  const colors: number[] = [];
  const color = new THREE.Color();
  for (const { points, color: hex } of polygons) {
    if (points.length < 3) continue;
    color.set(hex);
    for (let i = 1; i < points.length - 1; i++) {
      let [a, b, c] = [points[0], points[i], points[i + 1]];
      // normal = (b-a) x (c-a) must point up (+y), otherwise swap b and c
      if ((b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x) > 0) [b, c] = [c, b];
      for (const p of [a, b, c]) {
        positions.push(p.x, y, p.z);
        colors.push(color.r, color.g, color.b);
      }
    }
  }
  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
  geometry.setAttribute('color', new THREE.Float32BufferAttribute(colors, 3));
  geometry.computeVertexNormals();
  return new THREE.Mesh(geometry, new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 1 }));
}

/** Convex polygons extruded from the ground to `height` (sidewalk slabs with their curb), one mesh. */
function prismMesh(polygons: Point[][], height: number, hex: string): THREE.Mesh {
  const positions: number[] = [];
  for (const points of polygons) {
    if (points.length < 3) continue;
    for (let i = 1; i < points.length - 1; i++) {
      let [a, b, c] = [points[0], points[i], points[i + 1]];
      if ((b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x) > 0) [b, c] = [c, b];
      positions.push(a.x, height, a.z, b.x, height, b.z, c.x, height, c.z);
    }
    for (let i = 0; i < points.length; i++) {
      const a = points[i];
      const b = points[(i + 1) % points.length];
      positions.push(a.x, 0, a.z, b.x, 0, b.z, b.x, height, b.z, a.x, 0, a.z, b.x, height, b.z, a.x, height, a.z);
    }
  }
  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
  geometry.computeVertexNormals();
  // double sided: the walls are lit correctly whatever the winding of the polygon
  return new THREE.Mesh(geometry, new THREE.MeshStandardMaterial({ color: hex, roughness: 0.95, side: THREE.DoubleSide }));
}

interface Placement {
  x: number;
  y: number;
  z: number;
  rotY: number;
  scale?: [number, number, number];
  color?: string;
}

/** One InstancedMesh for many copies of a shape (thousands of street items stay one draw call). */
function instanced(geometry: THREE.BufferGeometry, material: THREE.Material, items: Placement[]): THREE.InstancedMesh {
  const mesh = new THREE.InstancedMesh(geometry, material, Math.max(items.length, 1));
  mesh.count = items.length;
  const matrix = new THREE.Matrix4();
  const q = new THREE.Quaternion();
  const up = new THREE.Vector3(0, 1, 0);
  const color = new THREE.Color();
  items.forEach((it, i) => {
    matrix.compose(new THREE.Vector3(it.x, it.y, it.z), q.setFromAxisAngle(up, it.rotY), new THREE.Vector3(...(it.scale ?? [1, 1, 1])));
    mesh.setMatrixAt(i, matrix);
    if (it.color) mesh.setColorAt(i, color.set(it.color));
  });
  mesh.instanceMatrix.needsUpdate = true;
  return mesh;
}

/** Road paint: thin white boxes. */
function markingsMesh(stripes: Stripe[], palette: Palette): THREE.InstancedMesh {
  return instanced(
    new THREE.BoxGeometry(1, 0.01, 1),
    new THREE.MeshStandardMaterial({ color: palette.paint, roughness: 0.6 }),
    stripes.map((s) => ({ x: s.x, y: 0.035, z: s.z, rotY: -s.angle, scale: [s.length, 1, s.width] })),
  );
}

/**
 * Vertical signage and lamps. `angle` of an item = direction it faces in the x/z plane:
 * a plate whose face is along +z is turned by pi/2 - angle, a box long along +x by -angle.
 */
function furnitureGroup(streets: Streets, palette: Palette): THREE.Group {
  const group = new THREE.Group();
  const dir = (f: Furniture, d: number) => ({ x: f.x + Math.cos(f.angle) * d, z: f.z + Math.sin(f.angle) * d });
  const face = (f: Furniture) => Math.PI / 2 - f.angle;
  const metal = new THREE.MeshStandardMaterial({ color: palette.metal, roughness: 0.6, metalness: 0.4 });
  const plate = (sides: number, r: number, turnUp = false) => {
    const g = new THREE.CylinderGeometry(r, r, 0.04, sides);
    if (sides === 8) g.rotateY(Math.PI / 8);
    return g.rotateX(turnUp ? -Math.PI / 2 : Math.PI / 2); // face along +z; triangles point down (up if turnUp)
  };

  // Poles (lamps 2.6, traffic lights 2.6, signs 1.7)
  const poles: Placement[] = [
    ...streets.lamps.map((f) => ({ x: f.x, y: CURB + 1.3, z: f.z, rotY: 0, scale: [1, 2.6, 1] as [number, number, number] })),
    ...streets.trafficLights.map((f) => ({ x: f.x, y: CURB + 1.3, z: f.z, rotY: 0, scale: [1, 2.6, 1] as [number, number, number] })),
    ...streets.signs.map((f) => ({ x: f.x, y: CURB + 0.85, z: f.z, rotY: 0, scale: [1, 1.7, 1] as [number, number, number] })),
  ];
  group.add(instanced(new THREE.CylinderGeometry(0.05, 0.06, 1, 6), metal, poles));

  // Lamp heads: arm towards the road with a warm light
  group.add(
    instanced(
      new THREE.BoxGeometry(0.6, 0.1, 0.2),
      new THREE.MeshStandardMaterial({ color: '#fff4cf', emissive: '#ffcf70', emissiveIntensity: palette.lampGlow }),
      streets.lamps.map((f) => ({ ...dir(f, 0.28), y: CURB + 2.6, rotY: -f.angle })),
    ),
  );
  // At night: a pool of warm light on the ground under every lamp (additive, no real lights:
  // a thousand point lights would be far too slow)
  if (palette.lampPools)
    group.add(
      instanced(
        new THREE.CircleGeometry(2.2, 24).rotateX(-Math.PI / 2),
        new THREE.MeshBasicMaterial({ color: '#ffb84d', transparent: true, opacity: 0.22, blending: THREE.AdditiveBlending, depthWrite: false }),
        streets.lamps.map((f) => ({ ...dir(f, 0.5), y: CURB + 0.02, rotY: 0 })),
      ),
    );

  // Traffic lights: dark housing + red/yellow/green lights facing the drivers
  group.add(
    instanced(
      new THREE.BoxGeometry(0.22, 0.75, 0.22),
      new THREE.MeshStandardMaterial({ color: '#23262a', roughness: 0.7 }),
      streets.trafficLights.map((f) => ({ x: f.x, y: CURB + 2.45, z: f.z, rotY: face(f) })),
    ),
  );
  const lights: Placement[] = streets.trafficLights.flatMap((f) =>
    ([['#e53935', 2.68], ['#fbc02d', 2.45], ['#43a047', 2.22]] as const).map(([color, y]) => ({ ...dir(f, 0.12), y: CURB + y, rotY: 0, color })),
  );
  group.add(instanced(new THREE.SphereGeometry(0.075, 10, 8), new THREE.MeshBasicMaterial({ color: '#ffffff' }), lights));

  // Signs
  const signs = (kind: string) => streets.signs.filter((s) => s.kind === kind);
  const at = (f: Furniture, d: number, y: number) => ({ ...dir(f, d), y: CURB + y, rotY: face(f) });
  const red = new THREE.MeshStandardMaterial({ color: '#c62828', roughness: 0.5 });
  const white = new THREE.MeshStandardMaterial({ color: '#fafafa', roughness: 0.5 });
  const blue = new THREE.MeshStandardMaterial({ color: '#1565c0', roughness: 0.5 });
  // stop: red octagon
  group.add(instanced(plate(8, 0.3), red, signs('stop').map((f) => at(f, 0.06, 1.55))));
  // give way: white triangle with a red border, pointing down
  group.add(instanced(plate(3, 0.42), red, signs('yield').map((f) => at(f, 0.06, 1.5))));
  group.add(instanced(plate(3, 0.3), white, signs('yield').map((f) => at(f, 0.085, 1.5))));
  // pedestrian crossing: blue square with a white triangle
  group.add(instanced(new THREE.BoxGeometry(0.55, 0.55, 0.04), blue, signs('crossing').map((f) => at(f, 0.06, 1.5))));
  group.add(instanced(plate(3, 0.2, true), white, signs('crossing').map((f) => at(f, 0.085, 1.5))));

  return group;
}

/** District name floating over its square, always readable (no depth test), main view only. */
function labelSprite(text: string, color: string, districtRadius: number, palette: Palette): THREE.Sprite {
  const canvas = document.createElement('canvas');
  const ctx = canvas.getContext('2d')!;
  const font = '600 56px system-ui, sans-serif';
  ctx.font = font;
  const width = Math.ceil(ctx.measureText(text).width) + 48;
  canvas.width = width;
  canvas.height = 88;
  ctx.font = font;
  ctx.fillStyle = palette.label.background;
  ctx.beginPath();
  ctx.roundRect(0, 0, width, 88, 44);
  ctx.fill();
  ctx.fillStyle = color;
  ctx.fillRect(22, 30, 14, 28);
  ctx.fillStyle = palette.label.text;
  ctx.textBaseline = 'middle';
  ctx.fillText(text, 44, 46);

  const texture = new THREE.CanvasTexture(canvas);
  texture.colorSpace = THREE.SRGBColorSpace;
  const sprite = new THREE.Sprite(new THREE.SpriteMaterial({ map: texture, depthTest: false, transparent: true }));
  const h = THREE.MathUtils.clamp(districtRadius * 0.12, 2.5, 9);
  sprite.scale.set((h * width) / 88, h, 1);
  sprite.renderOrder = 10;
  sprite.layers.set(LAYER_LABELS);
  return sprite;
}

/** Same family of color for a district, slightly different per building (deterministic, stable across renders). */
function variate(color: THREE.Color, i: number): THREE.Color {
  const r = Math.sin(i * 12.9898) * 43758.5453;
  const t = r - Math.floor(r);
  return color.offsetHSL((t - 0.5) * 0.03, (t - 0.5) * 0.1, (t - 0.5) * 0.16);
}
