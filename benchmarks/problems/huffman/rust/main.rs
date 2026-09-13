// Huffman benchmark: build a codec, encode deterministic symbols, and decode them.
// A sorted priority queue keeps codec construction O(k^2); encoding and
// decoding are linear in the input and encoded bit counts respectively.

use std::collections::BTreeMap;

const MODULUS: u64 = 1_000_000_007;

#[derive(Debug)]
enum Tree {
    Leaf {
        symbol: u8,
        weight: u64,
    },
    Branch {
        weight: u64,
        minimum_symbol: u8,
        left: Box<Tree>,
        right: Box<Tree>,
    },
}

impl Tree {
    fn weight(&self) -> u64 {
        match self {
            Tree::Leaf { weight, .. } | Tree::Branch { weight, .. } => *weight,
        }
    }

    fn minimum_symbol(&self) -> u8 {
        match self {
            Tree::Leaf { symbol, .. } => *symbol,
            Tree::Branch { minimum_symbol, .. } => *minimum_symbol,
        }
    }
}

fn comes_first(left: &Tree, right: &Tree) -> bool {
    left.weight() < right.weight()
        || (left.weight() == right.weight() && left.minimum_symbol() < right.minimum_symbol())
}

fn insert_tree(queue: &mut Vec<Box<Tree>>, tree: Box<Tree>) {
    let position = queue
        .iter()
        .position(|existing| comes_first(&tree, existing))
        .unwrap_or(queue.len());
    queue.insert(position, tree);
}

struct Codec {
    tree: Box<Tree>,
    codes: Vec<Option<u64>>,
}

impl Codec {
    fn new(data: &[u8]) -> Option<Self> {
        let mut frequencies = BTreeMap::<u8, u64>::new();
        for &symbol in data {
            *frequencies.entry(symbol).or_insert(0) += 1;
        }

        let mut queue = Vec::new();
        for (symbol, weight) in frequencies {
            insert_tree(&mut queue, Box::new(Tree::Leaf { symbol, weight }));
        }
        while queue.len() > 1 {
            let first = queue.remove(0);
            let second = queue.remove(0);
            let combined = Tree::Branch {
                weight: first.weight() + second.weight(),
                minimum_symbol: first.minimum_symbol().min(second.minimum_symbol()),
                left: first,
                right: second,
            };
            insert_tree(&mut queue, Box::new(combined));
        }

        let tree = queue.pop()?;
        let mut codes = vec![None; 32];
        build_codes(&tree, 0, 0, &mut codes);
        Some(Self { tree, codes })
    }

    fn encode(&self, data: &[u8]) -> Option<Vec<u8>> {
        let mut encoded = Vec::new();
        for &symbol in data {
            let code = self.codes.get(symbol as usize)?.as_ref()?;
            let length = code % 64;
            let bits = code / 64;
            for index in (0..length).rev() {
                encoded.push(((bits >> index) & 1) as u8);
            }
        }
        Some(encoded)
    }

    fn decode(&self, bits: &[u8]) -> Option<Vec<u8>> {
        if let Tree::Leaf { symbol, .. } = self.tree.as_ref() {
            return Some(vec![*symbol; bits.len()]);
        }

        let root = self.tree.as_ref();
        let mut current = root;
        let mut decoded = Vec::new();
        for &bit in bits {
            current = match current {
                Tree::Leaf { .. } => return None,
                Tree::Branch { left, right, .. } => {
                    if bit == 0 {
                        left
                    } else {
                        right
                    }
                }
            };
            if let Tree::Leaf { symbol, .. } = current {
                decoded.push(*symbol);
                current = root;
            }
        }
        Some(decoded)
    }
}

fn build_codes(tree: &Tree, bits: u64, length: u64, codes: &mut [Option<u64>]) {
    match tree {
        Tree::Leaf { symbol, .. } => {
            let code_length = if length == 0 { 1 } else { length };
            codes[*symbol as usize] = Some(bits * 64 + code_length);
        }
        Tree::Branch { left, right, .. } => {
            build_codes(left, bits * 2, length + 1, codes);
            build_codes(right, bits * 2 + 1, length + 1, codes);
        }
    }
}

fn choose_symbol(random: u64) -> u8 {
    match random % 1000 {
        0..=299 => 0,
        300..=479 => 1,
        480..=609 => 2,
        610..=709 => 3,
        710..=789 => 4,
        790..=849 => 5,
        850..=899 => 6,
        900..=939 => 7,
        _ => (8 + random % 24) as u8,
    }
}

fn generate_data(size: usize, seed: u64) -> Vec<u8> {
    let mut data = Vec::with_capacity(size);
    let mut state = seed;
    for _ in 0..size {
        state = (state * 1_103_515_245 + 12_345) % 2_147_483_648;
        data.push(choose_symbol(state));
    }
    data
}

fn checksum(values: &[u8]) -> (u64, u64) {
    let result = values.iter().enumerate().fold(0, |result, (index, value)| {
        (result + u64::from(*value) * (index as u64 + 1)) % MODULUS
    });
    (values.len() as u64, result)
}

fn argument(index: usize) -> u64 {
    std::env::args()
        .nth(index + 1)
        .expect("missing benchmark argument")
        .parse()
        .expect("benchmark argument must be an unsigned integer")
}

fn main() {
    let size = argument(0) as usize;
    let seed = argument(1);
    let repetitions = argument(2);
    assert!(size > 0 && repetitions > 0);

    let data = generate_data(size, seed);
    let expected_checksum = checksum(&data).1;
    let codec = Codec::new(&data).expect("Huffman codec requires non-empty input");
    let mut result = 0;
    for _ in 0..repetitions {
        let bits = codec.encode(&data).expect("input symbol missing from code table");
        let decoded = codec.decode(&bits).expect("encoded input ended within a code");
        let bit_result = checksum(&bits);
        let decoded_result = checksum(&decoded);
        assert_eq!(decoded_result.1, expected_checksum);
        let contribution =
            (bit_result.0 * 17 + bit_result.1 + decoded_result.1) % MODULUS;
        result = (result + contribution) % MODULUS;
    }
    println!("{result}");
}
