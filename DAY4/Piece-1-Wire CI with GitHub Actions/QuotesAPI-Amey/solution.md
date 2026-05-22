# Day 4 — Piece 1: Wire CI with GitHub Actions

**Submitted by:** amey2612  
**Branch:** `day4/piece1-ci-github-actions`  
**PR:** https://github.com/thinkbridge-thinkschool/ThinkSchoo-ameykhot-Day1/pull/new/day4/piece1-ci-github-actions

---

## ci.yml

```yaml
name: CI

on:
  push:
    branches: ["**"]
  pull_request:
    branches: [main]

jobs:
  build-and-test:
    name: Build & Test (.NET 10)
    runs-on: ubuntu-latest

    defaults:
      run:
        working-directory: "DAY4/Piece-1-Wire CI with GitHub Actions/QuotesAPI-Amey/Quotes.Tests.Unit"

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET 10
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "10.0.x"

      - name: Restore
        run: dotnet restore

      - name: Build
        run: dotnet build --no-restore

      - name: Test with coverage
        run: |
          dotnet test --no-build \
            --logger "trx;LogFileName=test-results.trx" \
            --collect:"XPlat Code Coverage" \
            --settings ../coverage.runsettings \
            --results-directory TestResults

      - name: Upload test results
        uses: actions/upload-artifact@v4
        if: always()
        with:
          name: test-results
          path: "DAY4/Piece-1-Wire CI with GitHub Actions/QuotesAPI-Amey/Quotes.Tests.Unit/TestResults/**/*.trx"

      - name: Upload coverage report
        uses: actions/upload-artifact@v4
        if: always()
        with:
          name: coverage-report
          path: "DAY4/Piece-1-Wire CI with GitHub Actions/QuotesAPI-Amey/Quotes.Tests.Unit/TestResults/**/coverage.cobertura.xml"

      - name: Check coverage >= 70%
        run: |
          COVERAGE_FILE=$(find TestResults -name "coverage.cobertura.xml" | head -1)
          if [ -z "$COVERAGE_FILE" ]; then
            echo "::error::No coverage.cobertura.xml found"
            exit 1
          fi
          python3 -c "
          import sys, xml.etree.ElementTree as ET
          root = ET.parse('$COVERAGE_FILE').getroot()
          rate = float(root.attrib.get('line-rate', '0'))
          pct = rate * 100
          print(f'Line coverage: {pct:.1f}%')
          if rate < 0.70:
              print(f'::error::Coverage {pct:.1f}% is below the required 70% threshold')
              sys.exit(1)
          print(f'Coverage {pct:.1f}% meets the >=70% requirement')
          "
```

---

## coverage.runsettings

```xml
<?xml version="1.0" encoding="utf-8"?>
<RunSettings>
  <DataCollectionRunSettings>
    <DataCollectors>
      <DataCollector friendlyName="XPlat Code Coverage">
        <Configuration>
          <Format>cobertura</Format>
          <Include>[QuotesApi]QuotesApi.Services.*,[QuotesApi]QuotesApi.Models.*,[QuotesApi]QuotesApi.Validators.*,[QuotesApi]QuotesApi.Time.*</Include>
          <ExcludeByAttribute>GeneratedCodeAttribute,CompilerGeneratedAttribute,ExcludeFromCodeCoverageAttribute</ExcludeByAttribute>
        </Configuration>
      </DataCollector>
    </DataCollectors>
  </DataCollectionRunSettings>
</RunSettings>
```

---

## What did you learn this session?

The thing that clicked: **GitHub Actions is just YAML describing a shell session on a fresh VM**. Once that model is clear, every workflow decision becomes obvious — each `run:` is just a bash command, `uses:` is a reusable function, and `if: always()` ensures artifacts are uploaded even when tests fail (which is exactly when you most need them).

The more subtle lesson was **coverage scoping**. Running `--collect:"XPlat Code Coverage"` against the whole assembly measures controllers, migrations, middleware — code the unit tests never touch — so the headline coverage number is meaninglessly low. Scoping to just the domain/service/validator layer via `<Include>` in `.runsettings` gives a number that actually reflects whether the business logic is covered.

---

## What would break this?

| Scenario | What breaks |
|---|---|
| A developer adds a new service class but no tests | Coverage drops below 70%, CI fails — which is the **intended** behaviour |
| The `coverage.runsettings` `<Include>` filter is too broad (e.g., adds `Data.*`) | DB-access code is now measured; untested repos drag coverage below threshold |
| A new project is added to the solution with its own test runner and the `defaults.run.working-directory` no longer points at the right folder | `dotnet restore` finds the wrong project, build/test fail |
| GitHub-hosted runners stop pre-installing Python 3 | The coverage threshold check step errors with `python3: command not found` |
| The `Quotes.Tests.Unit.csproj` drops the `coverlet.collector` package | `--collect:"XPlat Code Coverage"` silently produces no XML; the "find" step exits 1 with "No coverage file found" |
| Someone merges without the branch-protection rule configured | The "refuses to merge red CI" guarantee is gone — red PRs can be merged manually |
