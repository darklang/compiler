// Deterministic ray-tracing kernel over a fixed in-memory sphere scene.
// Rendering performs closest-hit intersection, hard shadows, and diffuse lighting.

const MODULUS: i64 = 1_000_000_007;

#[derive(Clone, Copy)]
struct Vec3 { x: f64, y: f64, z: f64 }

#[derive(Clone, Copy)]
struct Color { red: f64, green: f64, blue: f64 }

#[derive(Clone, Copy)]
struct Sphere { center: Vec3, radius: f64, color: Color }

#[derive(Clone, Copy)]
struct Ray { origin: Vec3, direction: Vec3 }

#[derive(Clone, Copy)]
struct Hit { distance: f64, sphere: Sphere }

impl Vec3 {
    fn new(x: f64, y: f64, z: f64) -> Self { Self { x, y, z } }
    fn add(self, other: Self) -> Self { Self::new(self.x + other.x, self.y + other.y, self.z + other.z) }
    fn subtract(self, other: Self) -> Self { Self::new(self.x - other.x, self.y - other.y, self.z - other.z) }
    fn scale(self, factor: f64) -> Self { Self::new(self.x * factor, self.y * factor, self.z * factor) }
    fn dot(self, other: Self) -> f64 { self.x * other.x + self.y * other.y + self.z * other.z }
    fn length(self) -> f64 { self.dot(self).sqrt() }
    fn normalize(self) -> Self { self.scale(1.0 / self.length()) }
}

fn color(red: f64, green: f64, blue: f64) -> Color { Color { red, green, blue } }

fn ray_sphere(ray: Ray, sphere: Sphere) -> Option<f64> {
    let offset = ray.origin.subtract(sphere.center);
    let half_b = offset.dot(ray.direction);
    let c = offset.dot(offset) - sphere.radius * sphere.radius;
    let discriminant = half_b * half_b - c;
    if discriminant < 0.0 { return None; }
    let root = discriminant.sqrt();
    let near = -half_b - root;
    let far = -half_b + root;
    if near > 0.001 { Some(near) } else if far > 0.001 { Some(far) } else { None }
}

fn closest_hit(spheres: &[Sphere], ray: Ray, maximum: f64) -> Option<Hit> {
    let mut maximum = maximum;
    let mut closest = None;
    for sphere in spheres.iter().copied() {
        if let Some(distance) = ray_sphere(ray, sphere) {
            if distance < maximum {
                maximum = distance;
                closest = Some(Hit { distance, sphere });
            }
        }
    }
    closest
}

fn is_shadowed(spheres: &[Sphere], point: Vec3, light: Vec3) -> bool {
    let toward_light = light.subtract(point);
    let light_distance = toward_light.length();
    let shadow_ray = Ray { origin: point, direction: toward_light.scale(1.0 / light_distance) };
    closest_hit(spheres, shadow_ray, light_distance).is_some()
}

fn shade(spheres: &[Sphere], ray: Ray, hit: Hit, light: Vec3) -> Color {
    let point = ray.origin.add(ray.direction.scale(hit.distance));
    let normal = point.subtract(hit.sphere.center).normalize();
    let surface = point.add(normal.scale(0.001));
    let light_direction = light.subtract(surface).normalize();
    let diffuse = 0.0_f64.max(normal.dot(light_direction));
    let intensity = if is_shadowed(spheres, surface, light) { 0.12 } else { 0.12 + 0.88 * diffuse };
    color(hit.sphere.color.red * intensity, hit.sphere.color.green * intensity, hit.sphere.color.blue * intensity)
}

fn trace(spheres: &[Sphere], ray: Ray, light: Vec3) -> Color {
    match closest_hit(spheres, ray, 1_000_000.0) {
        Some(hit) => shade(spheres, ray, hit, light),
        None => {
            let blend = 0.5 * (ray.direction.y + 1.0);
            color(0.08 + 0.12 * blend, 0.10 + 0.18 * blend, 0.16 + 0.30 * blend)
        }
    }
}

fn pixel_color(spheres: &[Sphere], light: Vec3, size: i64, x: i64, y: i64) -> Color {
    let denominator = (size - 1) as f64;
    let screen_x = 2.0 * x as f64 / denominator - 1.0;
    let screen_y = 1.0 - 2.0 * y as f64 / denominator;
    let camera = Vec3::new(0.0, 0.0, -5.0);
    let direction = Vec3::new(screen_x, screen_y, 1.5).normalize();
    trace(spheres, Ray { origin: camera, direction }, light)
}

fn render(spheres: &[Sphere], light: Vec3, size: i64) -> i64 {
    let mut result = 0_i64;
    for y in 0..size {
        for x in 0..size {
            let index = y * size + x;
            let pixel = pixel_color(spheres, light, size, x, y);
            let red = (pixel.red * 1_000_000.0) as i64;
            let green = (pixel.green * 1_000_000.0) as i64;
            let blue = (pixel.blue * 1_000_000.0) as i64;
            result = (result + (red * 3 + green * 5 + blue * 7) * (index + 1)).rem_euclid(MODULUS);
        }
    }
    result
}

fn argument(index: usize) -> i64 {
    std::env::args().nth(index + 1).expect("missing benchmark argument").parse().expect("benchmark argument must be an integer")
}

fn main() {
    let size = argument(0);
    let repetitions = argument(1);
    assert!(size >= 2 && repetitions > 0);
    let spheres = vec![
        Sphere { center: Vec3::new(0.0, 0.0, 0.0), radius: 1.0, color: color(0.90, 0.22, 0.18) },
        Sphere { center: Vec3::new(-1.45, -0.35, 1.3), radius: 0.65, color: color(0.18, 0.72, 0.30) },
        Sphere { center: Vec3::new(1.35, 0.15, 1.0), radius: 0.80, color: color(0.18, 0.38, 0.92) },
        Sphere { center: Vec3::new(0.0, -101.0, 1.5), radius: 100.0, color: color(0.72, 0.70, 0.62) },
    ];
    let light = Vec3::new(-4.0, 5.0, -3.0);
    let mut result = 0_i64;
    for _ in 0..repetitions {
        result = (result + render(&spheres, light, size)).rem_euclid(MODULUS);
    }
    println!("{result}");
}
