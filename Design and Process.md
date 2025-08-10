# Design and Process

---

## Git Branching Strategies Comparison

| Parameters | Git-flow | Github Lab (Extension of Trunk based Development) |
|------------|---------|-----------------------------------------------|
| **Summary of Usecase** | Gitflow is a branching strategy designed for projects with a more structured and formal release process. It emphasizes the separation of development work from release preparation and maintenance | GitHub Flow is a simpler, more lightweight branching strategy that focuses on continuous delivery and frequent releases. It is optimized for projects where the release process is less formal. |
| **Branches** | Main Branches:<br>main (or master): Production-ready code.<br>develop: Main integration branch for ongoing development.<br>Feature Branches: For new features/enhancements, branched from develop.<br>Release Branches: For release prep, branched from develop.<br>Hotfix Branches: For critical bug fixes, branched from main. | Main Branch:<br>main (or master): Production-ready code.<br>Feature Branches: For new features/bug fixes, branched from main. |
| **Workflow** | Features developed in isolation in feature branches and merged back into develop.<br>Release branches for stabilization.<br>Hotfix branches for urgent production fixes. | Features developed in isolated feature branches, code regularly committed and pushed.<br>Pull Requests (PRs) for proposing/reviewing changes.<br>PRs merged into main after approval and tests. |
| **Release Management** | Structured release process with dedicated release branches.<br>Suited for scheduled releases and release candidates. | No dedicated release branches.<br>Releases by tagging commits in main.<br>Suited for frequent/continuous deployment. |
| **Complexity** | More structured, more branches, can seem complex. | Simpler, easier to adopt/manage, especially for small/medium teams. |
| **On-Demand vs. Scheduled Release** | Supports scheduled releases via release branch stabilization. | Favors on-demand releases due to continuous integration. |
| **Number of Scrum Teams** | Can be complex with multiple teams, merging feature branches may require synchronization. | Scales well with multiple teams, allows parallel development and faster integration. |
| **Microservices** | Can be adapted, but may introduce coordination challenges during release phase. | Suitable for microservices, encourages frequent integration/deployment of small changes. |
| **Release Frequency** | Less frequent, larger releases. | Frequent, smaller releases. |
| **Code Stability** | Dedicated stabilization phase (release branch) for testing/bug-fixing. | Codebase more stable due to continuous integration, but requires robust automated testing. |
| **Feature Isolation** | Features isolated in feature branches until merged into release branch. | Encourages feature flags/toggles to isolate unfinished features. |
| **Hotfixes/Urgent Releases** | May require separate hotfix process, urgent changes can be complex to integrate. | Allows immediate hotfixes/urgent releases, changes continuously integrated. |
| **Visibility** | Clear separation between development/release phases, enhances visibility. | Better visibility into status of features/changes as they progress. |
| **Risk Management** | Mitigates risk via stabilization phases, but longer release cycles. | Lower risk due to continuous integration, but needs robust testing/monitoring. |

---

## UCX Platform
- Owned by Rajkumar Subaschandra bose
- Last updated: May 01, 2024 by Kayla Howell
- UCX is an EverNorth internal portal for clinicians, Nurses and MDs, who perform clinical reviews on UM requests.
- Supports Sleep, Gastro, DME, Radiology & Cardiology disciplines from eP and ImageOne platforms for MDs.
- Next milestone: Enable Nurses for these disciplines.
- Goal: Migrate all programs/users from CDP (IO platform) and eP clinical to UCX, retire CDP/eP Clinical.
- UCX will be available for enterprise needs as part of Clinical Care Management (CCM).
- Integration with TruCare or MHK products for Clinical Review.

---

## Logging Optimization & Cost Saving
- Owned by David Ovitz
- Last updated: Dec 05, 2023 by Vilesh Malhotra
- Log level recommendations:
  - Use configuration to control logging level per environment.
  - Default: "Warning" for DEV/INTG, can be changed as needed.
  - PROD: Higher log level recommended for mature domains/apps.
- Common library updates:
  - ucx.gravity.common >= 4.1.4
  - evicore.eventsource.cosmos >= 1.0.4
  - evicore.ucx.patterns >= 1.2.0
- Application updates:
  - Avoid logging PHI JSON objects.
  - Add supporting configuration for logging.
  - Example configuration for Serilog/Microsoft logging extensions provided.
  - Use environment variables to control log level config in terraform.
- Validations:
  - Query AppInsights/Log Analytics to validate logging.
  - Sample queries provided.

---

## API Service Auto Scaler
- Owned by David Ovitz
- Last updated: Dec 01, 2023
- Example from UCX.DiagnosisService (for API Services only)
- Minimum/maximum scale per environment; ensure enough available IPs on private endpoints.
- Example Terraform resource provided for autoscale settings.

---

## RU Settings by Container
- Owned by David Ovitz
- Last updated: Apr 05, 2024 by Colin Gilbert
- RUs on containers reduced to save costs, based on data analysis.
- For heavy load operations (replay/rehydration), scale up RUs, then scale back down after.
- Chart provided for new/old values.

---

## UCX - P2P Diagrams
- Owned by Vilesh Malhotra
- Last updated: May 29, 2024
- Business use case: Clinicians need to search/retrieve requests from multiple platforms and perform reviews.
- Logical diagram: Multiple UCX services for different roles, each with separate resource group.
- Private endpoints: All databases/keyvaults/SignalR services have restricted public access, app code hosted via App Services in private subnet.
- Integration VNETs: All App Services under stage-ucx-eu2-vnet, communication outside via integration VNETs.
- Application Gateway: Used for exposing endpoints outside VNET.
- Azure Front Door: Used for exposing micro frontends/static websites, managed WAF policy enabled.
- Common resource group for shared resources.

---

## Cosmos Rehydration - UCX Services
- Owned by Gopal Kamdi
- Dec 08, 2023
- Rehydration: Re-processing events for event sourcing (cosmos or any source), to resend messages/events as needed.
- When needed: Bump event version, correct mapping, add new fields to historical events.
- When not possible: If new fields not available in event source messages, need Kafka replay.
- Pre-requisites: Adjust RU’s on containers before rehydration.
- Endpoints for rehydration provided for DEV, INTG, PROD.

---

## Branching Strategy (Current Approach)
- Used to follow trunk-based strategy, still used for libraries.
- Now following a strategy with release/hotfix branches for production releases.
- Key points:
  - main matches PROD.
  - release/hotfix branches created with date for PROD releases.
  - feature/task branches from release/hotfix branches.
  - PR to release/hotfix only, not main; PR after functional testing and PO sign off.
  - Only release/hotfix branches deployed in INTG during regression testing.
- Possible modification: Only one release/hotfix branch at a time.
  - Pros: Short-lived branches, less confusion.
  - Cons: Stories ready but not planned for release cannot be merged to release branch.

---

## UCX Design Challenges
- Kafka Data Storage & Historical Data
  - Data since Nov 2022, plus historical data retained forever.
  - Replays/rehydration take unrealistic time.
  - Data manipulation, quality concerns.
  - Recommendations needed for data retention, schema updates, historical data sources.
- Event Driven Messaging Architecture
  - Good model, but complex and expensive for EviCore.
  - Future direction needed.
- UCX Design Review
  - Microservices design, currently too granular (25+ services).
  - Data duplication/replication, expensive build/code changes.
  - Recommendations needed to simplify/optimize.
- Cosmos DB Management
  - Dev engineers manage prod Cosmos DBs; other SQL DBs managed by IT SE.
  - Recommendations needed.
- Cloud Strategy
  - Need design standards/coding recommendations for cost optimization.
- Path to Production
  - Proactive review/guidance needed to avoid reactive work.
  - Tech stack review, technology recommendations, enterprise standards.

---

## Revisit Cache Busting
- Automated updates to micro-frontend static JS file URL in UCX.UI_2.
- Configuration/logic for file location enhanced to use version from static source.
- Example implementation and PRs provided.
- Approach allows UCX.UI_2 to remain unchanged for micro-frontend code changes after initial version URL addition.

---

## Discussion Items
- Lean coffee board.
- Completed:
  1. Approach to migrate ASE to PEP (Approach B recommended)
  2. Scaling for replay/rehydration, monitoring, auto scaling, RU impact
  3. App insights data cap, logging recommendations
- Pending:
  1. Rehydrates dropping Normalized RU consumption
  2. Product Ownership rebalancing, transition to service agnostic
  3. Trunk based development and git branching strategy
  4. Event versioning approach

---

## Example Terraform & Pipeline Snippets
- Example for azurerm_frontdoor_rules_engine (CacheBusting)
- Example for generating version file in pipeline

---

## Code Repo & Resource Group Table
| Code Repo | Resource Group | Deployment Pipeline | App Service/Storage Account | Application Insights | Database - Container | Dependencies/Notes | Team most familiar |
|-----------|---------------|--------------------|----------------------------|---------------------|---------------------|--------------------|-------------------|
| Activity Log Service | pd1_rsg_cus_ucx | https://dev.azure.com/eviCoreDev/UCX/_git/ActivityLogService | https://dev.azure.com/eviCoreDev/UCX/_build?definitionId=2798 | ehcuspd1-asp-activitylogservice | ehcuspd1-ai-ucx | eheu2pd1ucxcosmosdb - RequestForServiceEventsDB | App services and database are in different resource groups. App services are in resource group pd1_rsg_eu2_ucx-linux | Everest |

---

Thanks and Regards  
Siraj
