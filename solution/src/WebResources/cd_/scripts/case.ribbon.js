/*
 * CaseDesk - Case form command bar (web resource cd_/scripts/case.ribbon.js).
 *
 * Escalate button (RibbonDiff.xml on cd_case):
 *   - Enable rule  : CaseDesk.Ribbon.canEscalate(PrimaryControl) - case is active and not Resolved.
 *   - Command      : CaseDesk.Ribbon.escalate(PrimaryControl)
 *       1. opens cd_/html/escalate.html as a modal dialog (Xrm.Navigation.navigateTo, target 2)
 *          which returns the reason through sessionStorage (web resource dialogs have no return value);
 *       2. calls the bound Custom API cd_EscalateCase with Xrm.WebApi.online.execute;
 *       3. refreshes the form and shows the API's Message as a form notification.
 * The server (EscalateCasePlugin) is authoritative: this script only adds UX.
 */
/* global Xrm */
var CaseDesk = window.CaseDesk || {};

CaseDesk.Ribbon = CaseDesk.Ribbon || (function () {
    "use strict";

    var STATE_ACTIVE = 0;
    var STATUS_RESOLVED = 417200005;
    var FORM_TYPE_CREATE = 1;
    var DIALOG_WEBRESOURCE = "cd_/html/escalate.html";
    var NOTIFICATION_ID = "cd_escalate_result";

    function valueOf(formContext, attributeName) {
        var attribute = formContext.getAttribute(attributeName);
        return attribute ? attribute.getValue() : null;
    }

    /** Enable rule: only an existing, active, not-yet-resolved case can be escalated. */
    function canEscalate(primaryControl) {
        try {
            if (!primaryControl || !primaryControl.ui || !primaryControl.data) {
                return false;
            }
            if (primaryControl.ui.getFormType() === FORM_TYPE_CREATE) {
                return false;
            }
            return valueOf(primaryControl, "statecode") === STATE_ACTIVE &&
                valueOf(primaryControl, "statuscode") !== STATUS_RESOLVED;
        } catch (e) {
            return false;
        }
    }

    function newToken() {
        return "cd_escalate_" + Date.now() + "_" + Math.random().toString(36).slice(2);
    }

    function readDialogResult(token) {
        var raw = null;
        try {
            raw = window.sessionStorage.getItem(token);
            window.sessionStorage.removeItem(token);
        } catch (e) {
            raw = null;
        }
        if (!raw) {
            return { cancelled: true };
        }
        try {
            return JSON.parse(raw);
        } catch (e) {
            return { cancelled: true };
        }
    }

    /** Opens the reason dialog; resolves with { cancelled, reason }. */
    function askForReason(token, caseNumber) {
        var pageInput = {
            pageType: "webresource",
            webresourceName: DIALOG_WEBRESOURCE,
            data: "token=" + encodeURIComponent(token) + "&case=" + encodeURIComponent(caseNumber || "")
        };
        var navigationOptions = {
            target: 2,
            position: 1,
            width: { value: 480, unit: "px" },
            height: { value: 340, unit: "px" },
            title: "Escalate case"
        };
        return Xrm.Navigation.navigateTo(pageInput, navigationOptions).then(function () {
            return readDialogResult(token);
        });
    }

    /** Executes the bound Custom API cd_EscalateCase; resolves with { Escalated, Message }. */
    function executeEscalate(caseId, reason) {
        var parameterTypes = {
            entity: { typeName: "mscrm.cd_case", structuralProperty: 5 }
        };
        var request = {
            entity: { entityType: "cd_case", id: caseId }
        };
        if (reason) {
            request.Reason = reason;
            parameterTypes.Reason = { typeName: "Edm.String", structuralProperty: 1 };
        }
        request.getMetadata = function () {
            return {
                boundParameter: "entity",
                parameterTypes: parameterTypes,
                operationType: 0, // action
                operationName: "cd_EscalateCase"
            };
        };
        return Xrm.WebApi.online.execute(request).then(function (response) {
            if (!response.ok) {
                throw new Error(response.statusText || "The escalation failed.");
            }
            return response.json();
        });
    }

    function showError(error) {
        var message = (error && error.message) ? error.message : String(error);
        return Xrm.Navigation.openErrorDialog({ message: message });
    }

    /** Command: Escalate. */
    function escalate(primaryControl) {
        var formContext = primaryControl;
        if (!canEscalate(formContext)) {
            return Promise.resolve();
        }
        var caseId = formContext.data.entity.getId().replace(/[{}]/g, "");
        var caseNumber = valueOf(formContext, "cd_casenumber");
        var token = newToken();
        var saveFirst = formContext.data.entity.getIsDirty() ? formContext.data.save() : Promise.resolve();

        formContext.ui.clearFormNotification(NOTIFICATION_ID);
        return Promise.resolve(saveFirst)
            .then(function () { return askForReason(token, caseNumber); })
            .then(function (result) {
                if (!result || result.cancelled) {
                    return null;
                }
                Xrm.Utility.showProgressIndicator("Escalating case...");
                return executeEscalate(caseId, (result.reason || "").trim()).then(function (response) {
                    Xrm.Utility.closeProgressIndicator();
                    return response;
                }, function (error) {
                    Xrm.Utility.closeProgressIndicator();
                    throw error;
                });
            })
            .then(function (response) {
                if (!response) {
                    return null;
                }
                return formContext.data.refresh(false).then(function () {
                    formContext.ui.setFormNotification(
                        response.Message || (response.Escalated ? "Case escalated." : "Case is already escalated."),
                        response.Escalated ? "INFO" : "WARNING",
                        NOTIFICATION_ID);
                    if (formContext.ui.refreshRibbon) {
                        formContext.ui.refreshRibbon();
                    }
                });
            })
            .catch(showError);
    }

    return {
        canEscalate: canEscalate,
        escalate: escalate
    };
}());

window.CaseDesk = CaseDesk;
