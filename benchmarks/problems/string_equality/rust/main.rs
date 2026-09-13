// String equality benchmark: exercise identity, equal allocations, length
// mismatch, and early/late byte mismatches for short and long dynamic strings.

use std::env;

#[inline(never)]
fn operator_equals(left: &str, right: &str) -> bool {
    left == right
}

#[inline(never)]
fn api_equals(left: &str, right: &str) -> bool {
    left.eq(right)
}

fn contribution(matches: bool, weight: u64) -> u64 {
    if matches { weight } else { 0 }
}

fn main() {
    let args: Vec<String> = env::args().collect();
    let iterations: u64 = args[1].parse().expect("iterations must be an integer");
    let token = args[2].parse::<i64>().expect("token must be an integer").to_string();

    let short_left = format!("ab{token}cd");
    let short_equal = ["a", "b", &token, "cd"].concat();
    let short_length_mismatch = format!("ab{token}cdx");
    let short_first_mismatch = format!("xb{token}cd");
    let short_last_mismatch = format!("ab{token}ce");

    let long_middle = concat!(
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789",
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"
    );
    let long_left = format!("prefix:{token}:{long_middle}:suffix");
    let long_equal = ["pre", "fix:", &token, ":", long_middle, ":suffix"].concat();
    let long_length_mismatch = format!("prefix:{token}:{long_middle}:suffix!");
    let long_first_mismatch = format!("xrefix:{token}:{long_middle}:suffix");
    let long_last_mismatch = format!("prefix:{token}:{long_middle}:suffiy");

    let mut checksum = 0u64;
    for _ in 0..iterations {
        checksum += contribution(operator_equals(&short_left, &short_left), 1);
        checksum += contribution(operator_equals(&short_left, &short_equal), 2);
        checksum += contribution(operator_equals(&short_left, &short_length_mismatch), 4);
        checksum += contribution(operator_equals(&short_left, &short_first_mismatch), 8);
        checksum += contribution(operator_equals(&short_left, &short_last_mismatch), 16);
        checksum += contribution(api_equals(&long_left, &long_left), 32);
        checksum += contribution(api_equals(&long_left, &long_equal), 64);
        checksum += contribution(api_equals(&long_left, &long_length_mismatch), 128);
        checksum += contribution(api_equals(&long_left, &long_first_mismatch), 256);
        checksum += contribution(api_equals(&long_left, &long_last_mismatch), 512);
    }

    println!("{checksum}");
}
