# ADR-0001 — Dois repositórios (Front e Back) e banco como serviço

- **Status:** Aceito
- **Data:** 2026-10-04

## Contexto

O projeto de origem era um único PWA (arquivo único). A plataforma terá API, Web e Android, desenvolvidos por mais de uma pessoa. O briefing inicial previa um repositório único; a decisão do time foi separar **front**, **back** e **banco**.

## Decisão

- **`Ronat-ia_Games_Back`:** API ASP.NET Core, motor de jogos, migrações e seeds do banco, documentação da plataforma e ADRs.
- **`Ronat-ia_Games_Front`:** Web (PWA) e o projeto Android (Capacitor), que compartilham o mesmo código de interface.
- **Banco:** não é um repositório, é um serviço (PostgreSQL gerenciado no Supabase). O código que o define (migrações do EF Core, seeds, scripts de papéis) mora no repositório Back.

## Consequências

- (+) Pipelines, permissões e deploys independentes; histórico mais limpo em cada repositório.
- (−) Risco de o contrato entre as partes se afastar. **Mitigação:** o Back publica o contrato OpenAPI (`docs/openapi/v1.json`, verificado por teste no CI); o Front gera seus tipos a partir dele; mudanças que quebram o contrato exigem versão nova da API (`/api/v2`).
- (−) Mudanças que cruzam os dois lados exigem dois PRs. **Mitigação:** referenciar um ao outro e manter a API retrocompatível (campos novos sempre opcionais), porque APKs antigos continuam em uso.
- Os repositórios são **públicos**: nunca versionar segredos nem dados pessoais.
