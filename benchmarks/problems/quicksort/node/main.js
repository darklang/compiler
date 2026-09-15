// main.js - Parameterized Node.js benchmark implementation.
function argument(index) {
    const value = Number.parseInt(process.argv[index + 2], 10);
    if (!Number.isFinite(value)) throw new Error(`invalid benchmark argument ${index}`);
    return value;
}

// Quicksort Benchmark
// Sorts a list and returns a checksum

function quicksort(arr) {
    if (arr.length <= 1) {
        return arr;
    }
    const pivot = arr[Math.floor(arr.length / 2)];
    const left = arr.filter(x => x < pivot);
    const middle = arr.filter(x => x === pivot);
    const right = arr.filter(x => x > pivot);
    return [...quicksort(left), ...middle, ...quicksort(right)];
}

function generateList(n, seed) {
    const result = [];
    let x = BigInt(seed);
    for (let i = 0; i < n; i++) {
        x = (x * 1103515245n + 12345n) % (2n ** 31n);
        result.push(Number(x % 10000n));
    }
    return result;
}

function checksum(arr) {
    let result = 0;
    for (let i = 0; i < arr.length; i++) {
        result = (result + arr[i] * (i + 1)) % 1000000007;
    }
    return result;
}

const arr = generateList(argument(0), argument(1));
const sortedArr = quicksort(arr);
console.log(checksum(sortedArr));
