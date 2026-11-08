import { computeSlaState, formatDuration } from "../slaState";

const MIN = 60 * 1000;
const HOUR = 60 * MIN;
const DAY = 24 * HOUR;

const due = new Date("2026-11-10T12:00:00Z");
const warningOn = new Date(due.getTime() - 2 * HOUR);
const at = (offsetMs: number): Date => new Date(due.getTime() + offsetMs);

describe("formatDuration", () => {
    it.each([
        [0, "<1m"],
        [59 * 1000, "<1m"],
        [MIN, "1m"],
        [4 * MIN + 30 * 1000, "4m"],
        [59 * MIN, "59m"],
        [HOUR, "1h 00m"],
        [HOUR + 5 * MIN, "1h 05m"],
        [23 * HOUR + 59 * MIN, "23h 59m"],
        [DAY, "1d 0h"],
        [2 * DAY + 3 * HOUR + 40 * MIN, "2d 3h"],
        [-(HOUR + 5 * MIN), "1h 05m"],
    ])("formats %d ms as %s", (ms, expected) => {
        expect(formatDuration(ms)).toBe(expected);
    });
});

describe("computeSlaState", () => {
    it("returns none when there is no due date", () => {
        expect(computeSlaState(at(0), null, warningOn, null, null)).toEqual({ kind: "none", label: "No SLA" });
    });

    it("returns none for an invalid due date", () => {
        expect(computeSlaState(at(0), new Date(NaN), null, null, null).kind).toBe("none");
    });

    it("is green before the warning window", () => {
        const s = computeSlaState(at(-(2 * DAY + 3 * HOUR)), due, warningOn, null, null);
        expect(s.kind).toBe("green");
        expect(s.label).toBe("Due in 2d 3h");
        expect(s.remainingMs).toBe(2 * DAY + 3 * HOUR);
    });

    it("is green just before warningOn", () => {
        expect(computeSlaState(new Date(warningOn.getTime() - 1), due, warningOn, null, null).kind).toBe("green");
    });

    it("is amber at exactly warningOn", () => {
        const s = computeSlaState(new Date(warningOn.getTime()), due, warningOn, null, null);
        expect(s.kind).toBe("amber");
        expect(s.label).toBe("Due in 2h 00m");
    });

    it("is amber inside the warning window", () => {
        const s = computeSlaState(at(-(HOUR + 5 * MIN)), due, warningOn, null, null);
        expect(s).toEqual({ kind: "amber", label: "Due in 1h 05m", remainingMs: HOUR + 5 * MIN });
    });

    it("is still amber (not red) at exactly the due time", () => {
        const s = computeSlaState(at(0), due, warningOn, null, null);
        expect(s.kind).toBe("amber");
        expect(s.label).toBe("Due in <1m");
        expect(s.remainingMs).toBe(0);
    });

    it("stays green until due when warningOn is missing", () => {
        expect(computeSlaState(at(-MIN), due, null, null, null).kind).toBe("green");
        expect(computeSlaState(at(0), due, null, null, null).kind).toBe("green");
        expect(computeSlaState(at(1), due, null, null, null).kind).toBe("red");
    });

    it("is red once past due", () => {
        const s = computeSlaState(at(HOUR + 5 * MIN), due, warningOn, null, null);
        expect(s).toEqual({ kind: "red", label: "Overdue by 1h 05m", remainingMs: -(HOUR + 5 * MIN) });
    });

    it("is red 1 ms past due", () => {
        const s = computeSlaState(at(1), due, warningOn, null, null);
        expect(s.kind).toBe("red");
        expect(s.label).toBe("Overdue by <1m");
    });

    it("is met when resolved before due, regardless of now", () => {
        const s = computeSlaState(at(5 * DAY), due, warningOn, at(-HOUR), null);
        expect(s).toEqual({ kind: "met", label: "SLA met" });
    });

    it("is met when resolved at exactly due", () => {
        expect(computeSlaState(at(DAY), due, warningOn, at(0), null).kind).toBe("met");
    });

    it("is breached when resolved after due", () => {
        const s = computeSlaState(at(DAY), due, warningOn, at(1), null);
        expect(s).toEqual({ kind: "breached", label: "SLA breached" });
    });

    it("resolvedOn takes precedence over closedOn", () => {
        expect(computeSlaState(at(DAY), due, warningOn, at(-HOUR), at(HOUR)).kind).toBe("met");
    });

    it("is stopped when closed without resolution before due", () => {
        const s = computeSlaState(at(DAY), due, warningOn, null, at(-HOUR));
        expect(s).toEqual({ kind: "stopped", label: "SLA stopped" });
    });

    it("is stopped when closed at exactly due", () => {
        expect(computeSlaState(at(DAY), due, warningOn, null, at(0)).kind).toBe("stopped");
    });

    it("is breached when closed without resolution after due", () => {
        expect(computeSlaState(at(-DAY), due, warningOn, null, at(1)).kind).toBe("breached");
    });
});
