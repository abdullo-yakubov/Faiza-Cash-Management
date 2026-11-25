import { describe, expect, it } from 'vitest'
import { money, parseAmount, today } from './format.js'
import { can, menuFor } from './permissions.js'

describe('parseAmount', () => {
  it('принимает пробелы и запятую', () => {
    expect(parseAmount('1 250,5')).toBe(1250.5)
    expect(parseAmount('1000')).toBe(1000)
  })
  it('отклоняет неверные суммы', () => {
    for (const bad of ['', '0', '-5', '10.555', 'abc', '1e5', null]) expect(parseAmount(bad)).toBeNull()
  })
})

describe('money', () => {
  it('показывает два знака и валюту', () => {
    expect(money(750, 'TJS').replace(/\s/g, ' ')).toBe('750,00 TJS')
  })
})

describe('today', () => {
  it('считает дату по Душанбе (UTC+5)', () => {
    expect(today(new Date('2026-11-25T20:30:00Z'))).toBe('2026-11-26')
    expect(today(new Date('2026-11-25T18:59:00Z'))).toBe('2026-11-25')
  })
})

describe('права', () => {
  it('руководитель не проводит операции и не видит пользователей', () => {
    const manager = { role: 'Manager' }
    expect(can.operate(manager)).toBe(false)
    expect(menuFor(manager).map((m) => m.to)).not.toContain('/users')
    expect(menuFor(manager).map((m) => m.to)).toContain('/audit')
  })
  it('кассир проводит операции, но не управляет справочниками', () => {
    const cashier = { role: 'Cashier' }
    expect(can.operate(cashier)).toBe(true)
    expect(can.manageDirectories(cashier)).toBe(false)
    expect(menuFor(cashier).map((m) => m.to)).not.toContain('/audit')
  })
})
