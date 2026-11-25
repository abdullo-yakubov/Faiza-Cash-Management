import { useState } from 'react'
import { api } from '../lib/api.js'
import { counterpartyKinds, money } from '../lib/format.js'
import { Alert, Empty, Field, Modal, PageHead, useLoad, useSubmit } from '../components/ui.jsx'
import { useAuth } from '../auth.jsx'
import { can } from '../lib/permissions.js'

const CURRENCIES = ['TJS', 'USD', 'RUB', 'EUR']

export default function Directories() {
  const [tab, setTab] = useState('registers')
  const tabs = { registers: 'Кассы', branches: 'Филиалы', categories: 'Статьи', counterparties: 'Контрагенты' }
  return (
    <>
      <PageHead title="Справочники">
        <div className="segmented">
          {Object.entries(tabs).map(([k, v]) => <button key={k} className={tab === k ? 'on' : ''} onClick={() => setTab(k)}>{v}</button>)}
        </div>
      </PageHead>
      {tab === 'registers' && <Registers />}
      {tab === 'branches' && <Branches />}
      {tab === 'categories' && <Categories />}
      {tab === 'counterparties' && <Counterparties />}
    </>
  )
}

/** Общая таблица справочника с кнопками «Добавить» и «Изменить». */
function DirectoryTable({ list, columns, canEdit, renderForm, addLabel }) {
  const [editing, setEditing] = useState(null)
  return (
    <>
      {canEdit && <div className="toolbar"><button className="btn btn-primary" onClick={() => setEditing({})}>{addLabel}</button></div>}
      <Alert error={list.error} />
      {list.data?.length === 0 && <Empty>Пока пусто.</Empty>}
      {list.data?.length > 0 && (
        <div className="table-wrap">
          <table>
            <thead><tr>{columns.map((c) => <th key={c.title} className={c.num ? 'num' : ''}>{c.title}</th>)}{canEdit && <th />}</tr></thead>
            <tbody>
              {list.data.map((row) => (
                <tr key={row.id} className={row.isActive === false ? 'inactive' : ''}>
                  {columns.map((c) => <td key={c.title} className={c.num ? 'num' : ''}>{c.render(row)}</td>)}
                  {canEdit && <td><button className="btn btn-small" onClick={() => setEditing(row)}>Изменить</button></td>}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {editing && renderForm(editing, () => setEditing(null), list.reload)}
    </>
  )
}

function EditModal({ title, onClose, onSave, children }) {
  const { busy, error, submit } = useSubmit(async () => { await onSave(); onClose() })
  return (
    <Modal title={title} onClose={onClose}>
      <form onSubmit={submit}>
        <Alert error={error} />
        {children}
        <div className="row-actions">
          <button className="btn btn-primary" disabled={busy}>Сохранить</button>
          <button type="button" className="btn" onClick={onClose}>Отмена</button>
        </div>
      </form>
    </Modal>
  )
}

function ActiveBox({ value, onChange }) {
  return (
    <label className="check"><input type="checkbox" checked={value} onChange={(e) => onChange(e.target.checked)} /> Действует</label>
  )
}

const status = (r) => (r.isActive ? <span className="badge badge-ok">действует</span> : <span className="badge">отключено</span>)

function Registers() {
  const { user } = useAuth()
  const list = useLoad(() => api('/registers'), [])
  const branches = useLoad(() => api('/branches'), [])
  return (
    <DirectoryTable list={list} canEdit={can.manageStructure(user)} addLabel="Добавить кассу"
      columns={[
        { title: 'Филиал', render: (r) => r.branchName },
        { title: 'Касса', render: (r) => r.name },
        { title: 'Валюта', render: (r) => r.currency },
        { title: 'Остаток', num: true, render: (r) => money(r.balance) },
        { title: 'Смена', render: (r) => (r.hasOpenShift ? 'открыта' : '—') },
        { title: 'Статус', render: status },
      ]}
      renderForm={(row, close, reload) => <RegisterForm row={row} branches={branches.data || []} onClose={close} onSaved={reload} />} />
  )
}

function RegisterForm({ row, branches, onClose, onSaved }) {
  const [f, setF] = useState({ branchId: row.branchId || branches[0]?.id || '', name: row.name || '', currency: row.currency || 'TJS', isActive: row.isActive ?? true })
  const save = async () => {
    const body = { ...f, branchId: Number(f.branchId) }
    if (row.id) await api(`/registers/${row.id}`, { method: 'PUT', body })
    else await api('/registers', { method: 'POST', body })
    await onSaved()
  }
  return (
    <EditModal title={row.id ? 'Касса' : 'Новая касса'} onClose={onClose} onSave={save}>
      {branches.length === 0 && <Alert kind="warn">Сначала добавьте филиал.</Alert>}
      <Field label="Филиал">
        <select value={f.branchId} onChange={(e) => setF({ ...f, branchId: e.target.value })} required>
          {branches.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
        </select>
      </Field>
      <Field label="Название"><input value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} maxLength={100} required /></Field>
      <Field label="Валюта" hint="Валюту нельзя изменить после первой операции.">
        <select value={f.currency} onChange={(e) => setF({ ...f, currency: e.target.value })}>
          {CURRENCIES.map((c) => <option key={c}>{c}</option>)}
        </select>
      </Field>
      {row.id && <ActiveBox value={f.isActive} onChange={(v) => setF({ ...f, isActive: v })} />}
    </EditModal>
  )
}

function Branches() {
  const { user } = useAuth()
  const list = useLoad(() => api('/branches'), [])
  return (
    <DirectoryTable list={list} canEdit={can.manageStructure(user)} addLabel="Добавить филиал"
      columns={[
        { title: 'Название', render: (r) => r.name },
        { title: 'Адрес', render: (r) => r.address },
        { title: 'Статус', render: status },
      ]}
      renderForm={(row, close, reload) => <BranchForm row={row} onClose={close} onSaved={reload} />} />
  )
}

function Categories() {
  const { user } = useAuth()
  const list = useLoad(() => api('/categories'), [])
  return (
    <DirectoryTable list={list} canEdit={can.manageDirectories(user)} addLabel="Добавить статью"
      columns={[
        { title: 'Статья', render: (r) => r.name },
        { title: 'Вид', render: (r) => (r.kind === 'Income' ? <span className="plus">Приход</span> : <span className="minus">Расход</span>) },
        { title: 'Статус', render: status },
      ]}
      renderForm={(row, close, reload) => <CategoryForm row={row} onClose={close} onSaved={reload} />} />
  )
}

function Counterparties() {
  const { user } = useAuth()
  const list = useLoad(() => api('/counterparties'), [])
  return (
    <DirectoryTable list={list} canEdit={can.manageDirectories(user)} addLabel="Добавить контрагента"
      columns={[
        { title: 'Название', render: (r) => r.name },
        { title: 'Вид', render: (r) => counterpartyKinds[r.kind] },
        { title: 'Телефон', render: (r) => r.phone },
        { title: 'Примечание', render: (r) => r.note },
        { title: 'Статус', render: status },
      ]}
      renderForm={(row, close, reload) => <CounterpartyForm row={row} onClose={close} onSaved={reload} />} />
  )
}

function BranchForm({ row, onClose: close, onSaved: reload }) {
    const [f, setF] = useState({ name: row.name || '', address: row.address || '', isActive: row.isActive ?? true })
    const save = async () => {
      if (row.id) await api(`/branches/${row.id}`, { method: 'PUT', body: f })
      else await api('/branches', { method: 'POST', body: f })
      await reload()
    }
    return (
      <EditModal title={row.id ? 'Филиал' : 'Новый филиал'} onClose={close} onSave={save}>
        <Field label="Название"><input value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} maxLength={150} required /></Field>
        <Field label="Адрес"><input value={f.address} onChange={(e) => setF({ ...f, address: e.target.value })} maxLength={250} /></Field>
        {row.id && <ActiveBox value={f.isActive} onChange={(v) => setF({ ...f, isActive: v })} />}
      </EditModal>
    )
}

function CategoryForm({ row, onClose: close, onSaved: reload }) {
    const [f, setF] = useState({ name: row.name || '', kind: row.kind || 'Income', isActive: row.isActive ?? true })
    const save = async () => {
      if (row.id) await api(`/categories/${row.id}`, { method: 'PUT', body: f })
      else await api('/categories', { method: 'POST', body: f })
      await reload()
    }
    return (
      <EditModal title={row.id ? 'Статья' : 'Новая статья'} onClose={close} onSave={save}>
        <Field label="Название"><input value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} maxLength={100} required /></Field>
        <Field label="Вид">
          <select value={f.kind} onChange={(e) => setF({ ...f, kind: e.target.value })}>
            <option value="Income">Приход</option><option value="Expense">Расход</option>
          </select>
        </Field>
        {row.id && <ActiveBox value={f.isActive} onChange={(v) => setF({ ...f, isActive: v })} />}
      </EditModal>
    )
}

function CounterpartyForm({ row, onClose: close, onSaved: reload }) {
    const [f, setF] = useState({ name: row.name || '', kind: row.kind || 'Customer', phone: row.phone || '', note: row.note || '', isActive: row.isActive ?? true })
    const save = async () => {
      if (row.id) await api(`/counterparties/${row.id}`, { method: 'PUT', body: f })
      else await api('/counterparties', { method: 'POST', body: f })
      await reload()
    }
    return (
      <EditModal title={row.id ? 'Контрагент' : 'Новый контрагент'} onClose={close} onSave={save}>
        <Field label="Название / ФИО"><input value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} maxLength={150} required /></Field>
        <Field label="Вид">
          <select value={f.kind} onChange={(e) => setF({ ...f, kind: e.target.value })}>
            {Object.entries(counterpartyKinds).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
          </select>
        </Field>
        <Field label="Телефон"><input value={f.phone} onChange={(e) => setF({ ...f, phone: e.target.value })} maxLength={30} /></Field>
        <Field label="Примечание"><input value={f.note} onChange={(e) => setF({ ...f, note: e.target.value })} maxLength={500} /></Field>
        {row.id && <ActiveBox value={f.isActive} onChange={(v) => setF({ ...f, isActive: v })} />}
      </EditModal>
    )
}
