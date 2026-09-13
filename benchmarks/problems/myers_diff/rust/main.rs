// Myers shortest-edit-path benchmark over deterministic in-memory byte strings.
// Frontier layers are scanned sequentially, preserving O((N + M)D) time.

const MODULUS: i64 = 1_000_000_007;

use std::collections::HashMap;
use std::hash::{BuildHasherDefault, DefaultHasher};

type Frontier = HashMap<i64, i64, BuildHasherDefault<DefaultHasher>>;

struct DiffResult { distance: i64, work: i64 }

fn snake(left: &[u8], right: &[u8], mut x: i64, mut y: i64) -> (i64, i64) {
    while x < left.len() as i64 && y < right.len() as i64 && left[x as usize] == right[y as usize] {
        x += 1;
        y += 1;
    }
    (x, y)
}

fn myers(left: &[u8], right: &[u8]) -> DiffResult {
    let start = snake(left, right, 0, 0);
    let mut work = (start.0 + 1) * (start.1 + 3);
    if start.0 >= left.len() as i64 && start.1 >= right.len() as i64 {
        return DiffResult { distance: 0, work };
    }

    let mut previous = Frontier::default();
    previous.insert(0, start.0);
    for d in 1..=(left.len() + right.len()) as i64 {
        let mut current = Frontier::with_capacity_and_hasher(d as usize + 1, BuildHasherDefault::default());
        let mut reached = false;
        for diagonal in 0..=d {
            let k = -d + diagonal * 2;
            let choice = if k == -d {
                previous[&(k + 1)]
            } else if k == d {
                previous[&(k - 1)] + 1
            } else {
                let previous_left = previous[&(k - 1)];
                let previous_right = previous[&(k + 1)];
                if previous_left < previous_right { previous_right } else { previous_left + 1 }
            };
            let endpoint = snake(left, right, choice, choice - k);
            work = (work + (endpoint.0 + 1) * (endpoint.1 + 3) + (k + d + 1) * 17).rem_euclid(MODULUS);
            current.insert(k, endpoint.0);
            reached |= endpoint.0 >= left.len() as i64 && endpoint.1 >= right.len() as i64;
        }
        if reached { return DiffResult { distance: d, work }; }
        previous = current;
    }
    panic!("Myers search exceeded maximum edit distance")
}

fn argument(index: usize) -> i64 {
    std::env::args().nth(index + 1).expect("missing benchmark argument").parse().expect("benchmark argument must be an integer")
}

fn main() {
    let blocks = argument(0);
    let insertions = argument(1);
    let repetitions = argument(2);
    assert!(blocks > 0 && insertions > 0 && repetitions > 0);
    let unit = "darklang compiler benchmark: persistent values and recursive paths.\n";
    let prefix = unit.repeat(blocks as usize);
    let suffix = unit.repeat((blocks + 1) as usize);
    let insertion = "<changed-block>".repeat(insertions as usize);
    let left_text = prefix.clone() + &suffix;
    let right_text = prefix + &insertion + &suffix;
    let mut result = 0_i64;
    for _ in 0..repetitions {
        let diff = myers(left_text.as_bytes(), right_text.as_bytes());
        result = (result + diff.distance * 1_000_003 + diff.work).rem_euclid(MODULUS);
    }
    println!("{result}");
}
