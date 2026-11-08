/**
 * Pure SLA state logic for the CaseDesk SLA badge (FR-008, research R8).
 * No PCF / React dependencies so it can be unit tested with Jest.
 */

export type SlaKind = "none" | "green" | "amber" | "red" | "met" | "breached" | "stopped";

export interface SlaState {
    kind: SlaKind;
    label: string;
    /** Signed milliseconds until due (negative when overdue). Only set while the clock is running. */
    remainingMs?: number;
}

const MINUTE = 60 * 1000;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;

/**
 * Formats a duration compactly: "2d 3h", "1h 05m", "4m", "<1m".
 * Negative values are treated as their absolute value.
 */
export function formatDuration(ms: number): string {
    const abs = Math.abs(ms);
    if (abs < MINUTE) {
        return "<1m";
    }
    const days = Math.floor(abs / DAY);
    const hours = Math.floor((abs % DAY) / HOUR);
    const minutes = Math.floor((abs % HOUR) / MINUTE);
    if (days > 0) {
        return `${days}d ${hours}h`;
    }
    if (hours > 0) {
        return `${hours}h ${minutes.toString().padStart(2, "0")}m`;
    }
    return `${minutes}m`;
}

function isValid(d: Date | null | undefined): d is Date {
    return d instanceof Date && !isNaN(d.getTime());
}

export function computeSlaState(
    now: Date,
    due: Date | null,
    warningOn: Date | null,
    resolvedOn: Date | null,
    closedOn: Date | null
): SlaState {
    if (!isValid(due)) {
        return { kind: "none", label: "No SLA" };
    }
    const dueMs = due.getTime();

    if (isValid(resolvedOn)) {
        return resolvedOn.getTime() <= dueMs
            ? { kind: "met", label: "SLA met" }
            : { kind: "breached", label: "SLA breached" };
    }

    if (isValid(closedOn)) {
        return closedOn.getTime() > dueMs
            ? { kind: "breached", label: "SLA breached" }
            : { kind: "stopped", label: "SLA stopped" };
    }

    const remainingMs = dueMs - now.getTime();
    if (remainingMs < 0) {
        return { kind: "red", label: `Overdue by ${formatDuration(remainingMs)}`, remainingMs };
    }
    if (isValid(warningOn) && now.getTime() >= warningOn.getTime()) {
        return { kind: "amber", label: `Due in ${formatDuration(remainingMs)}`, remainingMs };
    }
    return { kind: "green", label: `Due in ${formatDuration(remainingMs)}`, remainingMs };
}
