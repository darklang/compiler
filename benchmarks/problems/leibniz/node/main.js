// main.js - Parameterized Node.js benchmark implementation.
function argument(index) {
    const value = Number.parseInt(process.argv[index + 2], 10);
    if (!Number.isFinite(value)) throw new Error(`invalid benchmark argument ${index}`);
    return value;
}

// Leibniz Pi Benchmark
// Computes pi using Leibniz formula: pi/4 = 1 - 1/3 + 1/5 - 1/7 + ...

function leibnizPi(n) {
    let sum = 0.0;
    let sign = 1.0;
    for (let i = 0; i < n; i++) {
        sum += sign / (2 * i + 1);
        sign = -sign;
    }
    return sum * 4.0;
}

// Iteration count is supplied by the runner.
// Output as integer (multiply by large factor for precision)
console.log(Math.floor(leibnizPi(argument(0)) * 100000000));
