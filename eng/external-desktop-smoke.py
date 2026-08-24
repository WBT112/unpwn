#!/usr/bin/env python3
"""Small semantic AT-SPI smoke for the externally observable desktop boundary."""

import argparse
import subprocess
import sys
import time

import pyatspi


TIMEOUT_SECONDS = 25


def parse_arguments():
    parser = argparse.ArgumentParser()
    parser.add_argument("--process-id", type=int, required=True)
    return parser.parse_args()


def descendants(node):
    if node is None:
        return
    yield node
    for index in range(node.childCount):
        yield from descendants(node.getChildAtIndex(index))


def process_id(node):
    try:
        return node.get_process_id()
    except (AttributeError, RuntimeError):
        return None


def attributes(node):
    result = {}
    try:
        for item in node.getAttributes():
            key, separator, value = item.partition(":")
            if separator:
                result[key.casefold()] = value
    except (AttributeError, RuntimeError):
        pass
    return result


def automation_id(node):
    try:
        accessible_id = node.get_accessible_id()
        if accessible_id:
            return accessible_id
    except (AttributeError, RuntimeError):
        pass
    values = attributes(node)
    return values.get("automation-id") or values.get("automationid") or values.get("id")


def find_by_automation_id(expected, app_process_id, timeout=TIMEOUT_SECONDS):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        desktop = pyatspi.Registry.getDesktop(0)
        if desktop is None:
            time.sleep(0.1)
            continue
        try:
            for node in descendants(desktop):
                if process_id(node) == app_process_id and automation_id(node) == expected:
                    return node
        except (AttributeError, RuntimeError):
            pass
        time.sleep(0.1)
    raise RuntimeError(f"Timed out waiting for semantic control: {expected}")


def wait_for_absence(expected, app_process_id):
    deadline = time.monotonic() + TIMEOUT_SECONDS
    while time.monotonic() < deadline:
        try:
            find_by_automation_id(expected, app_process_id, timeout=0.2)
        except RuntimeError:
            return
        time.sleep(0.1)
    raise RuntimeError(f"Semantic control stayed visible: {expected}")


def focus(node):
    node.queryComponent().grabFocus()


def invoke(automation_identifier, app_process_id):
    node = find_by_automation_id(automation_identifier, app_process_id)
    deadline = time.monotonic() + TIMEOUT_SECONDS
    while not node.getState().contains(pyatspi.STATE_ENABLED):
        if time.monotonic() >= deadline:
            raise RuntimeError(f"Semantic control stayed disabled: {automation_identifier}")
        time.sleep(0.1)
    focus(node)
    activate(node, automation_identifier)


def activate(node, description):
    actions = node.queryAction()
    for index in range(actions.nActions):
        name = actions.getName(index).casefold()
        if name in {"click", "press", "activate", "toggle", "select"}:
            if not actions.doAction(index):
                raise RuntimeError(f"AT-SPI rejected action for {description}")
            return
    raise RuntimeError(f"No semantic activation action for {description}")


def set_text(automation_identifier, value, app_process_id):
    node = find_by_automation_id(automation_identifier, app_process_id)
    focus(node)
    node.queryEditableText().setTextContents(value)


def choose_item(automation_identifier, item_identifier, app_process_id):
    node = find_by_automation_id(automation_identifier, app_process_id)
    focus(node)
    actions = node.queryAction()
    for index in range(actions.nActions):
        if "expand" in actions.getName(index).casefold():
            if not actions.doAction(index):
                raise RuntimeError(f"AT-SPI rejected expansion for {automation_identifier}")
            break
    else:
        raise RuntimeError(f"No semantic expansion action for {automation_identifier}")

    item = find_by_automation_id(item_identifier, app_process_id)
    for _ in range(3):
        try:
            activate(item, item_identifier)
            return
        except (NotImplementedError, RuntimeError):
            item = item.parent
    raise RuntimeError(f"No semantic selection action for {item_identifier}")


def close_application(app_process_id):
    subprocess.run(
        [
            "xdotool",
            "search",
            "--all",
            "--onlyvisible",
            "--pid",
            str(app_process_id),
            "--name",
            "^unpwn",
            "windowactivate",
            "--sync",
            "%@",
            "key",
            "--clearmodifiers",
            "Alt+F4",
        ],
        check=True,
        timeout=TIMEOUT_SECONDS,
    )


def acknowledge_visible_criteria(app_process_id):
    container = find_by_automation_id("workflow-criteria-acknowledge", app_process_id)
    checkboxes = [
        node
        for node in descendants(container)
        if node.getRole() == pyatspi.ROLE_CHECK_BOX
    ]
    if not checkboxes:
        raise RuntimeError("No visible recovery completion criteria were exposed through AT-SPI")
    for checkbox in checkboxes:
        focus(checkbox)
        actions = checkbox.queryAction()
        if not any(actions.doAction(index) for index in range(actions.nActions)):
            raise RuntimeError("A recovery criterion could not be activated through AT-SPI")
        deadline = time.monotonic() + TIMEOUT_SECONDS
        while not checkbox.getState().contains(pyatspi.STATE_CHECKED):
            if time.monotonic() >= deadline:
                raise RuntimeError("A recovery criterion did not persist its checked state")
            time.sleep(0.1)


def main():
    arguments = parse_arguments()
    app_process_id = arguments.process_id
    window = find_by_automation_id("unpwn-main-window", app_process_id)
    focus(window)

    invoke("vault-begin", app_process_id)
    invoke("vault-trusted-yes", app_process_id)
    invoke("vault-primary-action", app_process_id)
    set_text("vault-create-password", "desktop-e2e-only-482!", app_process_id)
    set_text("vault-create-password-confirm", "desktop-e2e-only-482!", app_process_id)
    invoke("vault-create-acknowledge", app_process_id)
    invoke("vault-create-submit", app_process_id)
    invoke("dashboard-create-session", app_process_id)
    invoke("import-open-csv", app_process_id)
    invoke("import-reviewed", app_process_id)
    choose_item("accounts-category", "Email", app_process_id)
    invoke("accounts-category-save", app_process_id)
    invoke("accounts-continue-recovery", app_process_id)
    invoke("dashboard-recommendation-open", app_process_id)

    try:
        find_by_automation_id("workflow-begin", app_process_id, timeout=1)
    except RuntimeError:
        pass
    else:
        invoke("workflow-begin", app_process_id)
    invoke("workflow-primary-action", app_process_id)
    try:
        find_by_automation_id("workflow-primary-action", app_process_id, timeout=2)
    except RuntimeError:
        pass
    else:
        invoke("workflow-primary-action", app_process_id)
    browser_close = find_by_automation_id(
        "recovery-browser-close", app_process_id, timeout=75
    )
    focus(browser_close)
    acknowledge_visible_criteria(app_process_id)
    invoke("workflow-done", app_process_id)
    invoke("confirmation-cancel", app_process_id)
    invoke("workflow-done", app_process_id)
    invoke("confirmation-confirm", app_process_id)
    invoke("recovery-browser-close", app_process_id)
    wait_for_absence("recovery-browser-close", app_process_id)

    focus(window)
    close_application(app_process_id)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as error:  # The outer desktop boundary returns one secret-sanitized failure.
        print(str(error), file=sys.stderr)
        sys.exit(1)
