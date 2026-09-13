// Small language benchmark with a hand-written lexer and Pratt parser that
// directly evaluates recursive expressions over an in-memory program.

const MODULUS: i64 = 1_000_000_007;

#[derive(Clone, Copy)]
enum Token {
    Number(i64), Variable(i64), Plus, Minus, Star, Slash,
    Less, LeftParen, RightParen, Semicolon,
}

#[derive(Clone, Copy)]
enum Operator { Add, Subtract, Multiply, Divide, LessThan }

fn lex(source: &[u8]) -> Vec<Token> {
    let mut tokens = Vec::new();
    let mut index = 0;
    while index < source.len() {
        let byte = source[index];
        if byte.is_ascii_whitespace() { index += 1; continue; }
        if byte.is_ascii_digit() {
            let mut value = 0_i64;
            while index < source.len() && source[index].is_ascii_digit() {
                value = value * 10 + (source[index] - b'0') as i64;
                index += 1;
            }
            tokens.push(Token::Number(value));
            continue;
        }
        if byte.is_ascii_alphabetic() {
            let start = index;
            while index < source.len() && source[index].is_ascii_alphabetic() { index += 1; }
            tokens.push(match &source[start..index] {
                b"x" => Token::Variable(0),
                b"y" => Token::Variable(1),
                _ => panic!("unknown identifier in Warden program"),
            });
            continue;
        }
        tokens.push(match byte {
            b'+' => Token::Plus, b'-' => Token::Minus, b'*' => Token::Star,
            b'/' => Token::Slash, b'<' => Token::Less, b'(' => Token::LeftParen,
            b')' => Token::RightParen, b';' => Token::Semicolon,
            _ => panic!("unknown character in Warden program"),
        });
        index += 1;
    }
    tokens
}

fn infix(token: Token) -> Option<(Operator, u8)> {
    match token {
        Token::Less => Some((Operator::LessThan, 1)),
        Token::Plus => Some((Operator::Add, 2)),
        Token::Minus => Some((Operator::Subtract, 2)),
        Token::Star => Some((Operator::Multiply, 3)),
        Token::Slash => Some((Operator::Divide, 3)),
        _ => None,
    }
}

struct Parser<'a> { tokens: &'a [Token], index: usize, x: i64, y: i64 }

impl Parser<'_> {
    fn primary(&mut self) -> i64 {
        let token = self.tokens[self.index];
        self.index += 1;
        match token {
            Token::Number(value) => value,
            Token::Variable(variable) => if variable == 0 { self.x } else { self.y },
            Token::LeftParen => {
                let expression = self.expression(0);
                assert!(matches!(self.tokens.get(self.index), Some(Token::RightParen)));
                self.index += 1;
                expression
            }
            _ => panic!("expected expression in Warden program"),
        }
    }

    fn expression(&mut self, minimum_precedence: u8) -> i64 {
        let mut left = self.primary();
        loop {
            let Some(token) = self.tokens.get(self.index).copied() else { break };
            let Some((operator, precedence)) = infix(token) else { break };
            if precedence < minimum_precedence { break; }
            self.index += 1;
            let right = self.expression(precedence + 1);
            left = apply_operator(operator, left, right);
        }
        left
    }

    fn evaluate_program(&mut self, iteration: i64) -> i64 {
        let mut result = 0_i64;
        let mut expression_index = 0_i64;
        while self.index < self.tokens.len() {
            self.x = (iteration * 17 + expression_index * 13).rem_euclid(97) + 3;
            self.y = (iteration * 29 + expression_index * 7).rem_euclid(89) + 5;
            let value = self.expression(0);
            assert!(matches!(self.tokens.get(self.index), Some(Token::Semicolon)));
            self.index += 1;
            result = (result + value * (expression_index + 1)).rem_euclid(MODULUS);
            expression_index += 1;
        }
        result
    }
}

fn apply_operator(operator: Operator, left: i64, right: i64) -> i64 {
    match operator {
        Operator::Add => left + right, Operator::Subtract => left - right,
        Operator::Multiply => left * right, Operator::Divide => left / right,
        Operator::LessThan => i64::from(left < right),
    }
}

fn argument(index: usize) -> i64 {
    std::env::args().nth(index + 1).expect("missing benchmark argument").parse().expect("benchmark argument must be an integer")
}

fn main() {
    let expression_count = argument(0);
    let repetitions = argument(1);
    assert!(expression_count > 0 && repetitions > 0);
    let statement = "x * x + y * 3 + (x + y) * (x - y) + x / 2;\n";
    let source = statement.repeat(expression_count as usize);
    let tokens = lex(source.as_bytes());
    let mut result = 0_i64;
    for iteration in 0..repetitions {
        let mut parser = Parser { tokens: &tokens, index: 0, x: 0, y: 0 };
        result = (result + parser.evaluate_program(iteration)).rem_euclid(MODULUS);
    }
    println!("{result}");
}
