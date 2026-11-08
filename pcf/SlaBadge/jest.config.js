/** @type {import('jest').Config} */
module.exports = {
    testEnvironment: "node",
    roots: ["<rootDir>/SlaBadge"],
    testMatch: ["**/__tests__/**/*.test.ts"],
    transform: {
        "^.+\\.tsx?$": ["ts-jest", { tsconfig: "<rootDir>/tsconfig.jest.json" }],
    },
    modulePathIgnorePatterns: ["<rootDir>/out/"],
};
