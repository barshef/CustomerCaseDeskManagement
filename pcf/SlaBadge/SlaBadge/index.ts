import { IInputs, IOutputs } from "./generated/ManifestTypes";
import { ISlaBadgeViewProps, SlaBadgeView } from "./SlaBadgeView";
import * as React from "react";

/** Read-only SLA countdown badge bound to cd_duedate (FR-008). */
export class SlaBadge implements ComponentFramework.ReactControl<IInputs, IOutputs> {
    /**
     * Used to initialize the control instance. The control is display-only and keeps no state.
     */
    public init(
        context: ComponentFramework.Context<IInputs>,
        notifyOutputChanged: () => void,
        state: ComponentFramework.Dictionary
    ): void {
        // Nothing to initialise; all inputs are read in updateView.
    }

    /**
     * Called when any value in the property bag has changed.
     * @returns root React element for the control
     */
    public updateView(context: ComponentFramework.Context<IInputs>): React.ReactElement {
        const p = context.parameters;
        const props: ISlaBadgeViewProps = {
            dueDate: p.dueDate.raw ?? null,
            warningOn: p.warningOn?.raw ?? null,
            resolvedOn: p.resolvedOn?.raw ?? null,
            closedOn: p.closedOn?.raw ?? null,
        };
        return React.createElement(SlaBadgeView, props);
    }

    /** The control never writes back to the bound field. */
    public getOutputs(): IOutputs {
        return {};
    }

    public destroy(): void {
        // React unmount (and the view's interval cleanup) is handled by the framework.
    }
}
