# unpwn Vision

## Purpose

unpwn helps people recover their digital identity after a suspected account compromise.

The problem is not only detecting an incident. The difficult part begins afterwards:

- Which accounts should be secured first?
- Which accounts are critical?
- Which recovery steps depend on access to another account?
- Which sessions, tokens, applications, or recovery methods must be reviewed?
- Which recovery steps have already been completed?
- How can the user avoid missing important accounts?

unpwn provides structure, prioritization, and guidance during this process.

## Primary Use Case

The primary use case is a private user who suspects that credentials, browser sessions, or account data may have been stolen through an infostealer, phishing attack, session hijacking, or a similar incident.

The user runs unpwn on a trusted device, imports or records affected accounts, secures critical identity accounts first, and works through service-specific recovery workflows until all relevant accounts have been reviewed.

## What unpwn is

unpwn is an open-source emergency account recovery assistant focused on recovery orchestration.

It supports users with:

- account discovery and inventory
- risk-based prioritization
- account dependencies
- recovery workflows
- authenticated password changes, password resets, and manual recovery paths
- credential rotation support
- session invalidation guidance
- MFA and recovery-option review
- progress tracking
- recovery history and documentation

unpwn does not replace a password manager. Password managers store credentials. unpwn helps users restore control after a security incident.

## What unpwn is not

unpwn is not:

- an antivirus product
- a malware scanner
- an infostealer detector
- a cloud account management platform
- an enterprise security product in the MVP
- an autonomous account takeover or recovery bot
- a general-purpose incident response suite

## Core Principles

### Local-first

Recovery data remains on the user's device in the MVP.

### Transparency

Users should understand every action performed or proposed by unpwn.

### Human control

Automation should accelerate recovery, not hide important security decisions.

### Open source

Security-sensitive software benefits from public review and transparent design.

### Maintainable automation

Automation is a supporting capability. The core value is the recovery workflow, prioritization, and progress management, not brittle website-specific automation.

### Repository-based collaboration

Recovery workflows are contributed, reviewed, tested, and released through pull requests to the main repository. unpwn does not rely on a runtime marketplace or downloaded third-party provider code.

### Platform-neutral core

Windows is the first target platform. Core recovery logic, workflows, and vault handling must remain portable to macOS and Linux.
