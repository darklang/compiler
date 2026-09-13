// Radix-2 FFT benchmark using recursive Cooley-Tukey decomposition.
// Each level performs linear splitting and combining, for O(n log n) work.

use std::f64::consts::TAU;

const MODULUS: i64 = 1_000_000_007;

#[derive(Clone, Copy)]
struct Complex {
    real: f64,
    imaginary: f64,
}

impl Complex {
    fn add(self, other: Self) -> Self {
        Self {
            real: self.real + other.real,
            imaginary: self.imaginary + other.imaginary,
        }
    }

    fn subtract(self, other: Self) -> Self {
        Self {
            real: self.real - other.real,
            imaginary: self.imaginary - other.imaginary,
        }
    }

    fn multiply(self, other: Self) -> Self {
        Self {
            real: self.real * other.real - self.imaginary * other.imaginary,
            imaginary: self.real * other.imaginary + self.imaginary * other.real,
        }
    }
}

fn fft(values: &[Complex]) -> Vec<Complex> {
    if values.len() <= 1 {
        return values.to_vec();
    }

    let mut evens = Vec::with_capacity(values.len() / 2);
    let mut odds = Vec::with_capacity(values.len() / 2);
    for (index, value) in values.iter().copied().enumerate() {
        if index % 2 == 0 {
            evens.push(value);
        } else {
            odds.push(value);
        }
    }
    let evens = fft(&evens);
    let odds = fft(&odds);
    let mut lower = Vec::with_capacity(values.len() / 2);
    let mut upper = Vec::with_capacity(values.len() / 2);
    for index in 0..evens.len() {
        let angle = -TAU * index as f64 / values.len() as f64;
        let twiddle = Complex {
            real: angle.cos(),
            imaginary: angle.sin(),
        };
        let product = twiddle.multiply(odds[index]);
        lower.push(evens[index].add(product));
        upper.push(evens[index].subtract(product));
    }
    lower.extend(upper);
    lower
}

fn generate_input(size: usize) -> Vec<Complex> {
    (0..size)
        .map(|index| {
            let x = index as f64;
            Complex {
                real: (x * 0.017).sin() + (x * 0.031).cos(),
                imaginary: (x * 0.013).cos() - (x * 0.007).sin(),
            }
        })
        .collect()
}

fn checksum(values: &[Complex]) -> i64 {
    values.iter().enumerate().fold(0, |result, (index, value)| {
        let quantized = ((value.real * 3.0 + value.imaginary * 5.0) * 1_000_000.0) as i64;
        (result + quantized * (index as i64 + 1)).rem_euclid(MODULUS)
    })
}

fn argument(index: usize) -> i64 {
    std::env::args()
        .nth(index + 1)
        .expect("missing benchmark argument")
        .parse()
        .expect("benchmark argument must be an integer")
}

fn main() {
    let size = argument(0) as usize;
    let repetitions = argument(1);
    assert!(size.is_power_of_two() && repetitions > 0);
    let input = generate_input(size);
    let mut result = 0;
    for _ in 0..repetitions {
        result = (result + checksum(&fft(&input))).rem_euclid(MODULUS);
    }
    println!("{result}");
}
