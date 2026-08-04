# unpwn Roadmap

## MVP 0.1 - Foundation

- Repository documentation
- Application structure
- Local encrypted storage foundation
- Testing setup
- Recovery session foundation

## MVP 0.2 - Recovery Engine

- Recovery session model
- Account model
- Recovery action workflow
- Account prioritization
- Status tracking
- Progress calculation
- Recovery history

## MVP 0.3 - Import

- Generic account import
- Browser/password manager export import support
- Mapping workflow

## MVP 0.4 - Recovery Providers

Initial providers:

1. Google
2. Microsoft
3. GitHub

Each provider defines recovery workflows such as:

- change or reset password
- invalidate sessions
- review MFA
- check recovery options
- review connected applications

## MVP 0.5 - Automation Assistance

- Browser assistance
- Visible Playwright workflows
- User-assisted automation
- Recovery location discovery

Automation remains a supporting feature. The primary product value is the recovery workflow and progress management.

## Future

Possible future integrations:

- password managers
- additional providers
- advanced recovery recommendations
- optional portability features
- additional automation capabilities
