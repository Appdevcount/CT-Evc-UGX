# Development and Release Process

---

## Story Pointing & Estimation
Scrum teams utilize the Fibonacci scale for story pointing, assigning points based on effort. This enables TPOs to align and inform upper management on deliverable estimates.

**Story Points Standardization:**
- **13:** TPO must break down effort to ensure manageability within a sprint
- **8:** 2 weeks or less
- **5:** 1 week or less
- **3:** 3 days or less
- **2:** 2 days or less
- **1:** 1 day or less

---

## Roles & Responsibilities
- **Technical Product Owners:**
  - Create standard stories per sprint (Administrative off sprint activities, Release Activities)
- **Scrum Masters:**
  - Automate task creation per story or ensure manual entry by developers
- **Engineering Team:**
  - Maintain each task, enter time spent, ensure correct status

---

## Specific Procedures
- **Administrative off Sprint Activities:** Track hours spent in meetings or non-sprint tasks
- **Release Activities:** Track hours spent in release activities
- Stories must be linked to an Administrative Feature provided by CTPO
- Each User Story in a Sprint must contain tasks for:
  - Analysis and Design
  - Development + Unit Testing
  - Peer Review
  - Functional Testing (Dev + Integration)
  - PO Sign off
  - Technical Documentation
- Engineering Team maintains tasks and enters hours daily
- Sprint Planning ensures all stories and tasks are ready for a clean start

---

## Sprint Process
- **Sprint Refinement:** For next sprint before start
- **Sprint Planning:** At start of sprint, involves estimation and team capacity planning
- **Sprint Release Readiness Call**

---

## Feature Example: Ability to view ePA requests within UCX
- **Feature 646957**
- Owned by: Sagar Shinde
- Last updated: Nov 16, 2023
- Status: In Progress

**Context:**
UCX system needs to view requests created by ePA system for clinical review and determinations.

**Decision Drivers:**
- Minimal/no impact on downstream consumers
- Handle historical requests
- Minimal impact on current workflows

**Current Workflow:**
- Request created in origin system (IO/CW/ePA) → CaseSaved event → XPLERSSCaseSavedEvent topic
- ERSS system enriches and publishes to XPLERSSEnrichedIOne & XPLERSSFinal topics
- UCX system consumes, creates UCX events, loaded by UCX microservices
- ePA requests lack proper event info in CaseSaved event

**Considered Options:**
- **Option 1:** Raise CaseSaved event with delta as null from ePA
  - **Scenario 1:** Handle new requests
  - **Pros:**
    - No code changes to UCX
    - Easy to implement/maintain
    - API can be reused across applications
  - **Cons:**
    - NA

---

## Dependencies on Teams
| Description | Environment | HI2 Team | XPG Team | Status |
|-------------|------------|----------|----------|--------|
| Need historical episodes data on XPLERSSFinalHistory topic for validation | Dev & INTG | Populate episode ids to XPLERSSEnrichedIOneHistory topic | Consume/process data and push to XPLERSSFinalHistory topic | Done on INTG |

---

## Current State & Challenges
| Topic Name | Environment | Description | Action Item | Remarks/Status |
|------------|------------|-------------|------------|---------------|
| XPLERSSEnrichedIOneHistory | Development | No data present | HI2 team to push data | Dev process not setup for HI2 team |

---

## UCX: Pre-Prod Validations
1. Azure AD account created in PROD (UCXCaseLock)
2. ResourceLockManagementB2C permissions checked
3. Client Secret ID generated and added in Azure Key vaults

**Prod Deployment Pipeline & Repos:**
| Repo Name | Pipeline URL | Owner |
|-----------|-------------|-------|
| UCX.CaseLockService | Pipelines - Run 20240108.2 |  |
| UCX.UI_2 | To be added once pipeline is finalized |  |

**Post Prod Validations:**
1. Check APIs in APIM (Get, Post, Delete)
2. Confirm lock creation/release with Business

**RollBack Plan:**
1. Re-deploy changes from main branch for repositories
2. Check APIs in APIM

---

## CDP: Pre-Prod Validations
1. ClientID key name: LockClientID
2. Secret Key name: LockClientSecret

**Prod Deployment Pipeline & Repos:**
| Sr No. | Repo Name | Pipeline URL |
|--------|-----------|--------------|
| 1 | CCN.CaseManagement |  |

**Post Prod Validations:**
1. Confirm lock creation/release with Business

**RollBack Plan:**
1. Re-deploy changes from main branch for repositories

---

## Routing Enhancement
- Owned by: Sagar Shinde
- Last updated: Apr 10, 2024 by Sirajudeen R
- Status: In Progress

**Context:**
Routing service requires frequent replays for onboarding or event version upgrades, taking 10-15 days and separate infrastructure.

**Current Workflow:**
- Medical discipline filter processes allowed disciplines
- History of events created for each request
- Internal replay processes events in sequence, only one version supported

**Considered Options:**
- **Option 1:** Remove medical discipline filter, support multiple event version processing
  - Remove filter, allow all disciplines
  - Add API filter for incremental onboarding
  - Process single latest version if multiple raised
  - Support multiple event version processing for internal replays
  - **Pros:**
    - No replay of kafka messages
    - Faster delivery
    - Reduced Cosmos RU consumption
  - **Cons:**
    - Increase in Index documents

---

## Cost Analysis
**CosmosDB Replay:**
- Regular RU: 9000
- Replay RU: 80000
- Replay period: 10 days
- Cost: $1363.2 per new program/version upgrade

**Azure Search Service:**
- Standard S1: 25GB, $0.34/hr
- Current INTG usage: 9MB/25GB (0.035% for 23000 docs)
- New S1 plan: 160GB
- S2 plan: 350GB, $1.35/hr

---

## Roles & Responsibilities Table
| Role | Responsibilities | Ownership/Handoff | Comment |
|------|------------------|-------------------|---------|
| Engineering Leads (Manager/Sr. Manager) | Staffing, IT goals, tech choices, standards, collaboration, metrics, talent, hiring, vendor management | Collaborate with SMs, P&P, Architecture, Business, IT Service Excellence | CTPO & Engineering Manager Partnership! |
| Engineering Leads | Technical SME, product/domain SME, road-mapping, decision assistance, PI planning, cross-team questions, best practices | Collaborate with CTPOs, IT Principal, Engineering Leaders |  |
| Dev Engineer | Advanced input, subject matter expert, design/code/test/debug, autonomous, judgment, mentoring | Member of scrum team |  |
| QA Engineer | Participate in ceremonies, shift left testing, acceptance criteria, story sizing, collaboration, functional/regression/automation testing | Member of scrum team |  |

---

Thanks and Regards  
Siraj
