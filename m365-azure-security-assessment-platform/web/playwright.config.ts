import { defineConfig } from "@playwright/test";

export default defineConfig({
    testDir: "./tests/e2e",
    outputDir: "./test-results",
    fullyParallel: false,
    workers: 1,
    retries: 0,
    reporter: [["list"], ["json", { outputFile: "test-results/results.json" }]],
    use: {
        baseURL: "http://127.0.0.1:3000",
        browserName: "chromium",
        screenshot: "only-on-failure",
        trace: "retain-on-failure",
    },
    projects: [
        { name: "desktop", use: { viewport: { width: 1440, height: 1000 } } },
        { name: "mobile", use: { viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true } },
    ],
});