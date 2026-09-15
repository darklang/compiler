// main.js - Parameterized Node.js benchmark implementation.
function argument(index) {
    const value = Number.parseInt(process.argv[index + 2], 10);
    if (!Number.isFinite(value)) throw new Error(`invalid benchmark argument ${index}`);
    return value;
}

// Collatz Benchmark
// Counts steps in Collatz sequences for numbers 1 to n

function collatzSteps(start) {
    let n = start;
    let steps = 0;
    while (n !== 1) {
        n = n % 2 === 0 ? n / 2 : 3 * n + 1;
        steps += 1;
    }
    return steps;
}

function sumCollatzRange(limit) {
    let total = 0;
    for (let i = 1; i <= limit; i++) {
        total += collatzSteps(i);
    }
    return total;
}

// The upper bound is supplied by the runner.
console.log(sumCollatzRange(argument(0)));
