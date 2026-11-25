import React, {useMemo, useState} from 'react';
import {ArrowDown, ArrowUp, ArrowUpDown} from 'lucide-react';
import {Pager} from './legacy.jsx';
import {EmptyState, SkeletonBlock} from './primitives.jsx';

/**
 * Таблица с сортировкой и пагинацией.
 * columns: [{ key, title, render?(row), align?: 'right', sortable?, sortValue?(row), className? }]
 * Режимы:
 *  - клиентский (по умолчанию): сортирует и режет rows сама;
 *  - серверный: передайте page, total и onPage — rows считаются текущей страницей.
 */
export function DataTable({columns, rows, rowKey = row => row.id, pageSize = 15, page, total, onPage, onRowClick, empty, caption, loading, rowClassName, initialSort}) {
  const [sort, setSort] = useState(initialSort || null); // {key, dir}
  const [localPage, setLocalPage] = useState(1);
  const server = typeof onPage === 'function' && total !== undefined;
  const sorted = useMemo(() => {
    if (!sort || server) return rows || [];
    const column = columns.find(c => c.key === sort.key);
    const value = column?.sortValue || (row => row[sort.key]);
    return [...(rows || [])].sort((a, b) => {
      const x = value(a), y = value(b);
      const result = typeof x === 'bigint' || typeof y === 'bigint' ? (x > y ? 1 : x < y ? -1 : 0) : typeof x === 'number' && typeof y === 'number' ? x - y : String(x ?? '').localeCompare(String(y ?? ''), 'ru', {numeric: true});
      return sort.dir === 'asc' ? result : -result;
    });
  }, [rows, sort, columns, server]);
  if (loading) return <SkeletonBlock rows={5}/>;
  const currentPage = server ? page : Math.min(localPage, Math.max(1, Math.ceil(sorted.length / pageSize)));
  const visible = server ? sorted : sorted.slice((currentPage - 1) * pageSize, currentPage * pageSize);
  const count = server ? total : sorted.length;
  const toggle = key => setSort(s => s?.key === key ? (s.dir === 'asc' ? {key, dir: 'desc'} : null) : {key, dir: 'asc'});
  return <>
    <div className="table-scroll">
      <table className="ui-table">
        {caption && <caption className="sr-only">{caption}</caption>}
        <thead><tr>{columns.map(column => {
          const active = sort?.key === column.key;
          const Icon = !active ? ArrowUpDown : sort.dir === 'asc' ? ArrowUp : ArrowDown;
          return <th key={column.key} scope="col" className={column.align === 'right' ? 'numeric' : undefined} aria-sort={column.sortable && !server ? (active ? (sort.dir === 'asc' ? 'ascending' : 'descending') : 'none') : undefined}>
            {column.sortable && !server ? <button type="button" className="ui-sort" onClick={() => toggle(column.key)}>{column.title}<Icon size={13} aria-hidden="true"/></button> : column.title}
          </th>;
        })}</tr></thead>
        <tbody>{visible.map(row => <tr key={rowKey(row)} className={[onRowClick ? 'clickable' : '', rowClassName?.(row) || ''].join(' ').trim() || undefined} onClick={onRowClick ? () => onRowClick(row) : undefined}>
          {columns.map(column => <td key={column.key} className={[column.align === 'right' ? 'numeric' : '', column.className || ''].join(' ').trim() || undefined}>{column.render ? column.render(row) : row[column.key]}</td>)}
        </tr>)}</tbody>
      </table>
      {!visible.length && (empty || <EmptyState title="Записей нет" text="Измените фильтры или добавьте запись."/>)}
    </div>
    {count > pageSize && <Pager index={currentPage} total={count} pageSize={pageSize} onChange={server ? onPage : setLocalPage}/>}
  </>;
}
