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
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { City } from '../../../core/api/api.models';
import { CityLayout, PlacedBuilding, computeCityLayout } from '../layout/city-layout';

/**
 * 3D city (three.js), ported from city_generator/src/main.js: orbit camera, hover outline and
 * tooltip. Buildings are one InstancedMesh (thousands of artists stay fast). Emits the clicked artist.
 */
@Component({
  selector: 'app-city-scene',
  template: `
    <canvas #canvas></canvas>
    @if (hovered(); as h) {
      <div class="tooltip" [style.left.px]="h.x" [style.top.px]="h.y">
        <strong>{{ h.building.name }}</strong><br />{{ h.building.genreName }}
      </div>
    }
  `,
  styleUrl: './city-scene.scss',
})
export class CityScene implements AfterViewInit {
  readonly city = input.required<City>();
  readonly selected = input<string | null>(null);
  readonly artistSelected = output<string>();

  protected readonly hovered = signal<{ building: PlacedBuilding; x: number; y: number } | null>(null);

  private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly zone = inject(NgZone);
  private readonly destroyRef = inject(DestroyRef);

  private renderer?: THREE.WebGLRenderer;
  private camera?: THREE.PerspectiveCamera;
  private controls?: OrbitControls;
  private readonly scene = new THREE.Scene();
  private cityGroup = new THREE.Group();
  private buildingsMesh?: THREE.InstancedMesh;
  private layout?: CityLayout;
  private readonly raycaster = new THREE.Raycaster();
  private readonly pointer = new THREE.Vector2();
  private readonly highlight = new THREE.Mesh(
    new THREE.BoxGeometry(1, 1, 1),
    new THREE.MeshBasicMaterial({ color: '#ffffff', side: THREE.BackSide }),
  );

  constructor() {
    // Rebuild the city whenever the data (e.g. the period) changes
    effect(() => {
      const city = this.city();
      if (this.renderer) this.buildCity(city);
    });
    effect(() => this.updateHighlight(this.selected()));
  }

  ngAfterViewInit(): void {
    const canvas = this.canvas().nativeElement;

    this.scene.background = new THREE.Color('#eee9e1');
    this.scene.fog = new THREE.Fog('#eee9e1', 60, 220);
    this.scene.add(new THREE.AmbientLight('#ffffff', 0.7));
    const sun = new THREE.DirectionalLight('#ffffff', 1.1);
    sun.position.set(20, 30, 10);
    this.scene.add(sun);
    this.highlight.visible = false;
    this.scene.add(this.highlight);

    this.camera = new THREE.PerspectiveCamera(45, 1, 0.1, 1000);
    this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));

    this.controls = new OrbitControls(this.camera, canvas);
    this.controls.enableDamping = true;
    this.controls.dampingFactor = 0.06;
    this.controls.maxPolarAngle = Math.PI / 2.3; // never below the ground

    this.buildCity(this.city());

    // Render loop and pointer handling outside Angular: no change detection at 60 fps
    this.zone.runOutsideAngular(() => {
      const resize = new ResizeObserver(() => this.resize());
      resize.observe(this.host.nativeElement);

      const onMove = (e: PointerEvent) => this.onPointerMove(e);
      const onClick = () => this.onClick();
      canvas.addEventListener('pointermove', onMove);
      canvas.addEventListener('click', onClick);

      let frame = 0;
      const loop = () => {
        frame = requestAnimationFrame(loop);
        this.controls!.update();
        this.renderer!.render(this.scene, this.camera!);
      };
      loop();

      this.destroyRef.onDestroy(() => {
        cancelAnimationFrame(frame);
        resize.disconnect();
        canvas.removeEventListener('pointermove', onMove);
        canvas.removeEventListener('click', onClick);
        this.controls?.dispose();
        this.disposeCity();
        this.renderer?.dispose();
      });
    });
  }

  private buildCity(city: City): void {
    this.disposeCity();
    this.layout = computeCityLayout(city);
    this.cityGroup = new THREE.Group();

    // Ground and district plates
    const side = Math.max(this.layout.width, this.layout.depth, 20) + 30;
    const ground = new THREE.Mesh(new THREE.PlaneGeometry(side, side), new THREE.MeshStandardMaterial({ color: '#d8d2c4' }));
    ground.rotation.x = -Math.PI / 2;
    this.cityGroup.add(ground);

    for (const d of this.layout.districts) {
      const plate = new THREE.Mesh(
        new THREE.BoxGeometry(d.width, 0.1, d.depth),
        new THREE.MeshStandardMaterial({ color: new THREE.Color(d.color).lerp(new THREE.Color('#d8d2c4'), 0.7) }),
      );
      plate.position.set(d.x, 0.05, d.z);
      this.cityGroup.add(plate);
    }

    // Buildings: one instance per artist, unit box scaled to width x height x width
    const buildings = this.layout.buildings;
    if (buildings.length > 0) {
      const mesh = new THREE.InstancedMesh(
        new THREE.BoxGeometry(1, 1, 1),
        new THREE.MeshStandardMaterial({ roughness: 0.9 }),
        buildings.length,
      );
      const matrix = new THREE.Matrix4();
      const color = new THREE.Color();
      buildings.forEach((b, i) => {
        matrix.compose(
          new THREE.Vector3(b.x, b.height / 2 + 0.1, b.z),
          new THREE.Quaternion(),
          new THREE.Vector3(b.width, b.height, b.width),
        );
        mesh.setMatrixAt(i, matrix);
        mesh.setColorAt(i, variate(color.set(b.color), i));
      });
      mesh.instanceMatrix.needsUpdate = true;
      this.buildingsMesh = mesh;
      this.cityGroup.add(mesh);
    }

    this.scene.add(this.cityGroup);

    // Frame the whole city
    const extent = Math.max(this.layout.width, this.layout.depth, 20);
    this.camera!.position.set(extent * 0.7, extent * 0.8, extent * 0.7);
    this.controls!.target.set(0, 2, 0);
    this.controls!.minDistance = 10;
    this.controls!.maxDistance = extent * 2.5;
    (this.scene.fog as THREE.Fog).far = extent * 3;
    this.updateHighlight(this.selected());
  }

  private onPointerMove(e: PointerEvent): void {
    const hit = this.pick(e);
    this.zone.run(() => this.hovered.set(hit ? { building: hit, x: e.offsetX, y: e.offsetY } : null));
    this.renderer!.domElement.style.cursor = hit ? 'pointer' : 'default';
  }

  private onClick(): void {
    const h = this.hovered();
    if (h) this.zone.run(() => this.artistSelected.emit(h.building.artistId));
  }

  private pick(e: PointerEvent): PlacedBuilding | null {
    if (!this.buildingsMesh || !this.layout) return null;
    const rect = this.renderer!.domElement.getBoundingClientRect();
    this.pointer.set(((e.clientX - rect.left) / rect.width) * 2 - 1, -((e.clientY - rect.top) / rect.height) * 2 + 1);
    this.raycaster.setFromCamera(this.pointer, this.camera!);
    const [hit] = this.raycaster.intersectObject(this.buildingsMesh, false);
    return hit?.instanceId !== undefined ? this.layout.buildings[hit.instanceId] : null;
  }

  /** White "inverted shell" outline around the selected building (technique of the prototype). */
  private updateHighlight(artistId: string | null): void {
    const b = artistId ? this.layout?.buildings.find((x) => x.artistId === artistId) : undefined;
    this.highlight.visible = !!b;
    if (b) {
      this.highlight.position.set(b.x, b.height / 2 + 0.1, b.z);
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
      if (o instanceof THREE.Mesh) {
        o.geometry.dispose();
        (o.material as THREE.Material).dispose();
      }
    });
    this.buildingsMesh = undefined;
  }
}

/** Same family of color for a district, slightly different per building (deterministic, stable across renders). */
function variate(color: THREE.Color, i: number): THREE.Color {
  const r = Math.sin(i * 12.9898) * 43758.5453;
  const t = r - Math.floor(r);
  return color.offsetHSL((t - 0.5) * 0.03, (t - 0.5) * 0.1, (t - 0.5) * 0.16);
}
