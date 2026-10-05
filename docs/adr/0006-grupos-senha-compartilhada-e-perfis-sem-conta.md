# ADR-0006 — Grupos: senha compartilhada, perfis sem conta e reivindicação

- **Status:** Aceito
- **Data:** 2026-10-05

## Contexto

O produto é para amigos e família. A entrada na conta é só pelo telefone (ADR-0003); então o que separa um grupo de outro é o **grupo**, não a pessoa. A decisão do dono do produto: quem cria o grupo "gera uma senha" que é compartilhada entre as pessoas que entram com o telefone. Além disso, os dados da família (jogadores, fotos, placares do app antigo) serão migrados depois, e muita gente ali **ainda não vai ter conta**: o histórico precisa existir antes de a pessoa aparecer, e ser dela quando ela aparecer.

## Decisão

- **A senha do grupo** é um código de 8 caracteres sorteado de um alfabeto de 30 símbolos, sem I, L, O, U, 0 e 1 (fácil de ditar). Dá cerca de 6,5 × 10^11 combinações. Fica guardada **em claro**: o dono e os membros precisam vê-la de novo para compartilhar (como um convite do Discord), então não há o que "hashear". Quem tem a senha entra; ela é única entre todos os grupos.
- **Controle da senha:** dono e administradores podem **desativar** (ninguém novo entra, a mesma senha volta ao reativar) ou **gerar outra** (a antiga morre na hora). Senha errada, desativada ou de grupo excluído respondem **igual** (`404 group.invalid_code`): não se descobre o que existe. Entrar e conferir a senha dividem um limite de 10 tentativas por minuto por pessoa; contra 6,5 × 10^11 combinações, adivinhar é inviável.
- **Três papéis:** dono (exatamente um ativo por grupo, garantido por índice único parcial), administrador e membro. O dono gerencia tudo, incluindo promover/rebaixar, transferir a propriedade e excluir o grupo; o administrador gerencia senha, nome e perfis sem conta e remove membros comuns; o membro participa e sai. O dono não sai nem é removido: transfere a propriedade ou exclui o grupo. A transferência rebaixa o dono atual a administrador e promove o outro, em duas etapas dentro de uma transação.
- **Um único modelo para pessoas e perfis:** `group_members` guarda **pessoas com conta** (`user_id` preenchido; nome e avatar são os da conta) e **perfis sem conta** (`user_id` nulo; nome e avatar próprios, geridos por dono/administrador, inclusive a foto). A linha **nunca é apagada**: sair ou ser removido só muda o `status`, para que partidas e placares continuem apontando para o membro. Voltar ao grupo reativa a mesma linha (como membro comum).
- **Reivindicação:** ao entrar com a senha, a pessoa pode escolher um perfil sem conta e **assumi-lo** (`claimMemberId`). A mesma linha passa a ter `user_id`, mantendo o histórico, e deixa de ter nome e foto próprios (vale a conta). Não há aprovação do dono: quem tem a senha já é "de dentro", e o dono pode remover quem não devia estar. Duas pessoas assumindo o mesmo perfil ao mesmo tempo: uma vence, a outra recebe `409` (concorrência otimista por `version`). Assumir não aumenta o grupo, então um grupo cheio ainda aceita.
- **Quem não é membro recebe 404**, nunca 403: o servidor não revela que um grupo existe. Excluir um grupo é **lógico** (`deleted_at`), a senha para de valer e o histórico fica no banco.
- **Limites** (protegem o banco e a memória gratuitos, configuráveis em `Groups:*`): 20 grupos ativos por pessoa, 100 membros ativos por grupo (contando perfis), 10 grupos criados por hora e por pessoa.
- **Excluir a conta** (LGPD): quem é dono de grupo com outras pessoas precisa transferir antes (`409 user.owns_groups`); os grupos em que é a única pessoa com conta são excluídos junto; os demais vínculos são encerrados. As linhas ficam (apontando para a conta anonimizada), o histórico continua íntegro.

## Consequências

- (+) O fluxo é o que o dono pediu: criar, compartilhar a senha, entrar com o telefone. Importar a família depois é só criar perfis sem conta; cada pessoa assume o seu ao entrar.
- (+) Regras críticas ficam no banco, não só no código: um dono ativo por grupo, uma linha por pessoa e grupo, perfil consistente com ter conta, formato da senha (`CHECK`s e índices únicos parciais, com testes).
- (−) A senha em claro significa que qualquer membro pode repassá-la. É o risco do convite compartilhado; a mitigação é gerar outra senha (e remover quem não deve estar). Não há "senha por pessoa".
- (−) Reivindicar sem aprovação deixa quem tem a senha assumir o perfil de outra pessoa do grupo. Num grupo de amigos e família a confiança já está na senha; se isso virar problema, o passo seguinte é a aprovação do dono (já cabe no modelo: bastaria um estado intermediário).
- (−) Quem sai e volta não consegue assumir um perfil na volta (a linha antiga e a do perfil teriam de ser fundidas, o que só faz sentido com histórico real). Fica para quando houver necessidade.
