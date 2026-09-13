// Compact regex parser and bounded, non-backtracking matcher benchmark.
// The supported syntax is literals, '.', ranges, alternation, and * + ?.

const MODULUS: i64 = 1_000_000_007;

#[derive(Clone, Copy)]
enum Atom { Literal(u8), AnyByte, Range(u8, u8) }

#[derive(Clone, Copy)]
enum Quantifier { ExactlyOne, ZeroOrMore, OneOrMore, ZeroOrOne }

#[derive(Clone, Copy)]
struct Piece { atom: Atom, quantifier: Quantifier }

fn parse(pattern: &[u8]) -> Vec<Vec<Piece>> {
    let mut alternatives = Vec::new();
    let mut pieces = Vec::new();
    let mut index = 0;
    while index < pattern.len() {
        if pattern[index] == b'|' {
            alternatives.push(pieces);
            pieces = Vec::new();
            index += 1;
            continue;
        }
        let atom = if pattern[index] == b'.' {
            index += 1;
            Atom::AnyByte
        } else if pattern[index] == b'[' {
            assert!(index + 4 < pattern.len() && pattern[index + 2] == b'-' && pattern[index + 4] == b']');
            let atom = Atom::Range(pattern[index + 1], pattern[index + 3]);
            index += 5;
            atom
        } else {
            let atom = Atom::Literal(pattern[index]);
            index += 1;
            atom
        };
        let quantifier = if index < pattern.len() {
            match pattern[index] {
                b'*' => { index += 1; Quantifier::ZeroOrMore }
                b'+' => { index += 1; Quantifier::OneOrMore }
                b'?' => { index += 1; Quantifier::ZeroOrOne }
                _ => Quantifier::ExactlyOne,
            }
        } else { Quantifier::ExactlyOne };
        pieces.push(Piece { atom, quantifier });
    }
    alternatives.push(pieces);
    alternatives
}

fn atom_matches(atom: Atom, byte: u8) -> bool {
    match atom {
        Atom::Literal(expected) => byte == expected,
        Atom::AnyByte => true,
        Atom::Range(low, high) => byte >= low && byte <= high,
    }
}

fn add_star_positions(atom: Atom, text: &[u8], mut position: usize, positions: &mut Vec<usize>) {
    positions.push(position);
    while position < text.len() && atom_matches(atom, text[position]) {
        position += 1;
        positions.push(position);
    }
}

fn matches_sequence(pieces: &[Piece], text: &[u8], start: usize) -> bool {
    let mut positions = vec![start];
    for piece in pieces {
        let mut next = Vec::new();
        for position in positions {
            match piece.quantifier {
                Quantifier::ExactlyOne => {
                    if position < text.len() && atom_matches(piece.atom, text[position]) { next.push(position + 1); }
                }
                Quantifier::ZeroOrOne => {
                    next.push(position);
                    if position < text.len() && atom_matches(piece.atom, text[position]) { next.push(position + 1); }
                }
                Quantifier::ZeroOrMore => add_star_positions(piece.atom, text, position, &mut next),
                Quantifier::OneOrMore => {
                    if position < text.len() && atom_matches(piece.atom, text[position]) {
                        add_star_positions(piece.atom, text, position + 1, &mut next);
                    }
                }
            }
        }
        if next.is_empty() { return false; }
        positions = next;
    }
    !positions.is_empty()
}

fn matches_any(alternatives: &[Vec<Piece>], text: &[u8], start: usize) -> bool {
    alternatives.iter().any(|pieces| matches_sequence(pieces, text, start))
}

fn scan(alternatives: &[Vec<Piece>], text: &[u8]) -> (i64, i64) {
    let mut count = 0_i64;
    let mut checksum = 0_i64;
    for position in 0..text.len() {
        if matches_any(alternatives, text, position) {
            checksum = (checksum + (position as i64 + 1) * (count + 3)).rem_euclid(MODULUS);
            count += 1;
        }
    }
    (count, checksum)
}

fn argument(index: usize) -> i64 {
    std::env::args().nth(index + 1).expect("missing benchmark argument").parse().expect("benchmark argument must be an integer")
}

fn main() {
    let blocks = argument(0);
    let repetitions = argument(1);
    assert!(blocks > 0 && repetitions > 0);
    let pattern = b"dark[a-z]*lang|compiler[0-9]+|ab[0-9]?";
    let alternatives = parse(pattern);
    let unit = "darklang darkxxlang compiler42 compiler ab ab7 nope DARKlang compilerx\n";
    let text = unit.repeat(blocks as usize);
    let mut result = 0_i64;
    for _ in 0..repetitions {
        let found = scan(&alternatives, text.as_bytes());
        result = (result + found.0 * 1_000_003 + found.1).rem_euclid(MODULUS);
    }
    println!("{result}");
}
