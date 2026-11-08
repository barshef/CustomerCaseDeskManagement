import * as React from "react";
import { Badge, BadgeProps } from "@fluentui/react-components";
import { computeSlaState, SlaKind } from "./slaState";

export interface ISlaBadgeViewProps {
    dueDate: Date | null;
    warningOn: Date | null;
    resolvedOn: Date | null;
    closedOn: Date | null;
}

/** How often the countdown re-renders. */
export const TICK_MS = 30 * 1000;

const KIND_STYLE: Record<SlaKind, Pick<BadgeProps, "color" | "appearance">> = {
    none: { color: "subtle", appearance: "outline" },
    green: { color: "success", appearance: "filled" },
    amber: { color: "warning", appearance: "filled" },
    red: { color: "danger", appearance: "filled" },
    met: { color: "success", appearance: "tint" },
    breached: { color: "danger", appearance: "tint" },
    stopped: { color: "informative", appearance: "filled" },
};

export const SlaBadgeView: React.FC<ISlaBadgeViewProps> = (props) => {
    const [now, setNow] = React.useState<Date>(() => new Date());

    React.useEffect(() => {
        const handle = window.setInterval(() => setNow(new Date()), TICK_MS);
        return () => window.clearInterval(handle);
    }, []);

    const state = computeSlaState(now, props.dueDate, props.warningOn, props.resolvedOn, props.closedOn);
    const style = KIND_STYLE[state.kind];

    return (
        <Badge
            size="large"
            shape="rounded"
            color={style.color}
            appearance={style.appearance}
            role="status"
            aria-label={`SLA status: ${state.label}`}
            title={`SLA status: ${state.label}`}
            data-sla-kind={state.kind}
        >
            {state.label}
        </Badge>
    );
};
