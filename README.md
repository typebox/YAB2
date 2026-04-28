# 🌀 YAB (Your AI Builder) CLI

> **Living Documentation & AI-Driven Business Logic Auditing for .NET**

YAB is a next-generation "Living Documentation" tool designed to bridge the gap between business requirements and technical implementation. It uses AI agents to audit your code against its intended purpose, ensuring that what you write is what the business actually needs.

---

## 🚀 Quick Start

### Prerequisites
- **.NET SDK 10.0+**
- **AI Agent Tooling**: Ensure you have an AI CLI (like `gemini-cli`) installed and configured.

### Installation & Execution
You can run YAB using the provided PowerShell script or directly via the .NET CLI:

#### Using PowerShell (Recommended)
```powershell
# Run the documentation and audit pipeline
.\yab.ps1 dev docs .

# Start the local documentation server
.\yab.ps1 dev serve .
```

#### Using .NET CLI
```powershell
dotnet run --project Yab.Cli dev docs .
```

---

## 🛠️ Core Commands

The YAB CLI is organized under the `dev` namespace to signify its role in the development lifecycle.

### `yab dev docs <path>`
The primary command for YAB. It orchestrates the entire pipeline:
1.  **Scan**: Discovers `[Concept]` and `[Intent]` attributes in your C# code.
2.  **Verify**: Checks cryptographic anchors to ensure code integrity.
3.  **Audit**: Calls upon AI agents to verify that code changes align with business logic.
4.  **Generate**: Produces a premium, interactive `LivingDocumentation.html` portal.

### `yab dev readme <path>`
Serves the project's `README.md` file as a beautifully styled HTML page. This is a great way to preview your documentation changes in a browser.

### `yab dev serve <path>`
Launches a lightweight local web server to host the generated documentation portal (`LivingDocumentation.html`). This allows for real-time inspection of your project's "Living Wiki."

### `yab dev sign-off <path>`
Used to programmatically "sign off" on verified code sections. This command updates the `BUILD_CERTIFICATE.md` with fresh cryptographic hashes (Physical Anchors), signaling that the AI and developers have validated the current state.

---

## ⚙️ Flags & Options

| Flag | Shorthand | Description |
| :--- | :--- | :--- |
| `--verbose` | `-v` | Enables detailed logging of the internal pipeline steps and AI agent communication. |
| `--manual` | `-m` | Forces manual audit mode, saving AI prompts to `.yab/prompts` for human review. |
| `--skip-ai` | | Skips the AI auditing step. Useful for fast documentation regeneration. |

---

## 🧠 The YAB Philosophy: "Grug Brain" Architecture

YAB follows a minimalist, convention-over-configuration philosophy inspired by the "Grug Brain" developer:

1.  **Code for Doing**: Keep C# logic clean and focused. Use `[Concept]` and `[Intent]` attributes to link code to the Wiki.
2.  **Markdown for Explaining**: Use Markdown files for deep business rationale, playbooks, and architectural "idears."
3.  **Physical Anchors**: Use `[yab-hash:...]` tags in your Markdown to lock documentation to specific code versions.
4.  **AI for Auditing**: Let LLMs handle the tedious task of checking if the code actually matches the requirements.

---

## 📁 Project Structure

- **`Yab.Cli/`**: The core CLI tool.
- **`Yab.Attributes/`**: Lightweight attributes for code-to-wiki linking.
- **`LivingDocumentation.html`**: The generated static portal.
- **`BUILD_CERTIFICATE.md`**: The source of truth for code integrity and sign-offs.

---

*Built with ❤️ for developers who value clarity, integrity, and simplicity.*
