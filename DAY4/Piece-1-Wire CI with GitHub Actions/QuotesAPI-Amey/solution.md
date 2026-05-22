# Day 4 — Piece 1: Wire CI with GitHub Actions

## Task

`.github/workflows/ci.yml`. Triggered on push to any branch and on PRs to main.

Steps: checkout, setup-dotnet 10, dotnet restore, dotnet build --no-restore, dotnet test --no-build --logger "trx;LogFileName=test-results.trx" --collect:"XPlat Code Coverage". Upload test results and coverage as artifacts. Fail the job on any test failure or coverage below 70%.

Add a status check requirement on the main branch so PRs can't merge without green CI. ("Refuses to merge red CI" — one of the values_behavior competencies.)

---

## Exercise

### Paste your ci.yml + a screenshot/link of the green CI run.

**Green CI run:**
https://github.com/thinkbridge-thinkschool/ThinkSchoo-ameykhot-Day1/actions

**ci.yml:**

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

### Paste your code or commit hash here…

**Commit:** `5fdec83`

**Branch:** `day4/piece1-ci-github-actions`

**Repo:** https://github.com/thinkbridge-thinkschool/ThinkSchoo-ameykhot-Day1

---

## What did you learn this session?

GitHub Actions is just YAML describing a shell session on a fresh VM. Once that clicked, every decision became obvious — `run:` is a bash command, `uses:` is a reusable function, and `if: always()` uploads artifacts even when tests fail (which is exactly when you need them most).

The subtler lesson was **coverage scoping**. Running `--collect:"XPlat Code Coverage"` against the whole assembly measures controllers, migrations, middleware — code the unit tests never touch — so the headline number is meaninglessly low. Scoping to the domain/service/validator layer via `<Include>` in a `.runsettings` file gives a number that actually reflects whether the business logic is covered.

---

## What would break this?

Dropping the `coverlet.collector` NuGet from the test project — `--collect:"XPlat Code Coverage"` silently produces no XML and the threshold step fails immediately with "No coverage file found". The fix is invisible at compile time, which makes it an easy thing to accidentally delete during a cleanup.
